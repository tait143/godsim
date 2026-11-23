using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Yearly espionage AI. Civilizations in contact with more advanced (or hostile) rivals grow a spy network
    /// (Curiosity, Mercantilism via trade partners, population) and spend it on at most one risky operation per run:
    /// <list type="bullet">
    /// <item>STEAL TECH from a more advanced civ (a tech the target knows, the thief can research and lacks;
    /// current-age first, cheapest first). Success learns it exactly like research would: unlocks, prog.dirty, event.</item>
    /// <item>SABOTAGE (aggressive civs, only while at war or Hostile): cripple a non-wonder building or burn a granary.</item>
    /// </list>
    /// Failures can EXPOSE the ring: a -20..-40 opinion impulse, a casus belli flag on the victim
    /// (<see cref="EspionageKeys.ExposedBy"/>), half the network lost, and a chronicle scandal. Victims raise their guard.
    /// Never operates between allies. Deterministic (ctx.Rng, index iteration) and allocation-free except chronicle text.
    /// </summary>
    public sealed class EspionageSystem : ISimSystem
    {
        private enum OpKind : byte { None, Steal, Sabotage }

        private readonly EspionageSettings _settings;

        public EspionageSystem() : this(null) { }

        public EspionageSystem(EspionageSettings settings) { _settings = settings ?? new EspionageSettings(); }

        /// <summary>Active tuning (mutable for tests/modding before the simulation starts).</summary>
        public EspionageSettings Settings => _settings;

        public string Name => "AI.Espionage";
        public int TickInterval => 96;

        public void Initialize(SimContext ctx) { }

        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            float years = Math.Max(1, ctx.DeltaTicks) / (float)SimTime.TicksPerYear;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c != null && c.IsAlive) DecayGuard(c, years);
            }
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c != null && c.IsAlive) RunCivilization(ctx, c, years);
            }
        }

        // =====================================================================================================
        // per civilization
        // =====================================================================================================
        private void RunCivilization(SimContext ctx, Civilization civ, float years)
        {
            var state = ctx.State;
            var db = ctx.Db;
            var civs = state.Civilizations.Items;
            var dip = state.Diplomacy;

            // ---- survey contacts
            bool hasRival = false;
            int tradeContacts = 0;
            for (int j = 0; j < civs.Count; j++)
            {
                var other = civs[j];
                if (other == null || !other.IsAlive || other.Id == civ.Id) continue;
                if (!dip.TryGet(civ.Id, other.Id, out var rel) || rel == null || !rel.HasContact) continue;
                if (rel.TradeAgreement) tradeContacts++;
                if (rel.Stance == DiplomaticStance.Allied) continue;
                if (IsMoreAdvanced(other, civ) || IsHostile(rel)) hasRival = true;
            }

            // ---- grow (or wither) the network
            float network = ReadNetwork(civ);
            if (hasRival)
            {
                float curiosity = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
                float merc = AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);
                int trade = Math.Min(tradeContacts, Math.Max(0, _settings.MaxTradeContacts));
                float tradeFactor = 1f + _settings.TradeContactBonus * trade * (0.5f + merc);
                float popFactor = MathUtil.Clamp((float)Math.Sqrt(Math.Max(0, PopulationOf(state, civ))) / 8f, 0.25f, 2f);
                network += (_settings.BaseGrowthPerYear + _settings.CuriosityGrowthPerYear * curiosity) * tradeFactor * popFactor * years;
            }
            else
            {
                network -= network * MathUtil.Clamp01(_settings.IdleDecayPerYear * years);
            }
            network = MathUtil.Clamp(MathUtil.Sanitize(network), 0f, Math.Max(0f, _settings.MaxNetwork));
            civ.Memory[EspionageKeys.Network] = network;
            if (!hasRival) return;

            // ---- pick the single best operation (at most one per civ per run)
            float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float curio = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            OpKind bestOp = OpKind.None;
            Civilization bestTarget = null;
            Relation bestRel = null;
            int bestTech = -1;
            float bestScore = float.MinValue, bestChance = 0f;
            long tick = state.Tick;

            for (int j = 0; j < civs.Count; j++)
            {
                var other = civs[j];
                if (other == null || !other.IsAlive || other.Id == civ.Id) continue;
                if (!dip.TryGet(civ.Id, other.Id, out var rel) || rel == null || !rel.HasContact) continue;
                if (rel.Stance == DiplomaticStance.Allied) continue;          // never between allies
                if (IsLayingLow(civ, other, tick)) continue;                   // recently burned there

                bool advanced = IsMoreAdvanced(other, civ);
                bool sabotage = network >= _settings.SabotageCost && CanSabotage(db, civ, rel) && other.SettlementIds.Count > 0;
                bool steal = advanced && network >= _settings.StealCost;
                if (!steal && !sabotage) continue;

                float offense = network * (rel.TradeAgreement ? 1f + _settings.TradeCoverBonus : 1f);
                float chance = SuccessChance(offense, CounterIntelligence(state, db, other));
                if (chance < MinAcceptableChance(db, civ, rel)) continue;     // not worth risking the agents yet

                if (steal)
                {
                    int tech = SelectTechToSteal(db, civ, other);
                    if (tech >= 0)
                    {
                        float gap = Math.Max(0, (int)other.Age - (int)civ.Age);
                        float score = (0.5f + 0.5f * curio + 0.1f * gap) * chance + ctx.Rng.Range(0f, 0.2f);
                        if (score > bestScore) { bestScore = score; bestOp = OpKind.Steal; bestTarget = other; bestRel = rel; bestTech = tech; bestChance = chance; }
                    }
                }
                if (sabotage)
                {
                    float score = (aggression + (rel.AtWar ? 0.5f : 0.1f)) * chance + ctx.Rng.Range(0f, 0.2f);
                    if (score > bestScore) { bestScore = score; bestOp = OpKind.Sabotage; bestTarget = other; bestRel = rel; bestTech = -1; bestChance = chance; }
                }
            }

            if (bestOp == OpKind.Steal) ExecuteSteal(ctx, civ, bestTarget, bestRel, bestTech, bestChance, network);
            else if (bestOp == OpKind.Sabotage) ExecuteSabotage(ctx, civ, bestTarget, bestRel, bestChance, network);
        }

        // =====================================================================================================
        // operations
        // =====================================================================================================
        private void ExecuteSteal(SimContext ctx, Civilization thief, Civilization target, Relation rel, int techId, float chance, float network)
        {
            var tech = ctx.Db.Tech(techId);
            if (tech == null) return;
            SpendOperation(ctx, thief, network, _settings.StealCost);
            if (ctx.Rng.Chance(chance))
            {
                if (!LearnTech(ctx, thief, techId)) return;
                Bump(thief, EspionageKeys.TechsStolen);
                RaiseGuard(target, _settings.GuardOnTheft);
                ctx.Ops?.AddChronicle(ChronicleCategory.Technology,
                    Adjective(ctx.Db, thief.Race) + " spies stole the secret of " + tech.Name + " from the " + Victims(ctx.Db, thief, target) + ".",
                    thief.Id, -1, Vec2i.Invalid, 1);
                return;
            }
            if (!RollExposure(ctx, target)) { RaiseGuard(target, _settings.GuardOnFailedAttempt); return; }
            Expose(ctx, thief, target, rel, -1, Vec2i.Invalid,
                "The " + target.Name + " unmasked " + Adjective(ctx.Db, thief.Race) + " spies hunting for the secret of " + tech.Name +
                ". Outrage swept their court, and the " + thief.Name + " stand accused.");
        }

        private void ExecuteSabotage(SimContext ctx, Civilization saboteur, Civilization target, Relation rel, float chance, float network)
        {
            var state = ctx.State;
            var rng = ctx.Rng;
            var s = PickSettlement(state, target, rng);
            if (s == null) return;
            Building building = PickSabotageBuilding(state, ctx.Db, s, rng);
            bool granary = HasStockedGranary(state, s);
            if (building == null && !granary) return;                             // nothing worth hitting: keep the network
            bool burnFood = granary && (building == null || rng.Chance(0.5f));

            SpendOperation(ctx, saboteur, network, _settings.SabotageCost);
            string adj = Adjective(ctx.Db, saboteur.Race);
            if (rng.Chance(chance))
            {
                string text;
                if (burnFood)
                {
                    float food = s.Stockpile.Get(ResourceType.Food);
                    float amount = MathUtil.Clamp(food * rng.Range(_settings.FoodBurnMin, _settings.FoodBurnMax), Math.Min(5f, food), Math.Max(0f, _settings.MaxFoodBurn));
                    s.Stockpile.ConsumeUpTo(ResourceType.Food, MathUtil.Sanitize(amount));
                    text = adj + " saboteurs set the granaries of " + s.Name + " ablaze.";
                }
                else
                {
                    float dmg = rng.Range(_settings.SabotageDamageMin, _settings.SabotageDamageMax);
                    float floor = MathUtil.Clamp01(_settings.MinConditionAfterSabotage);
                    building.Condition = MathUtil.Clamp(MathUtil.Sanitize(building.Condition - dmg, floor), floor, 1f);
                    text = adj + " saboteurs wrecked the " + BuildingName(ctx.Db, building.Type) + " of " + s.Name + ".";
                }
                Bump(saboteur, EspionageKeys.Sabotages);
                RaiseGuard(target, _settings.GuardOnSabotage);
                ctx.Ops?.AddChronicle(ChronicleCategory.War, text, saboteur.Id, s.Id, s.Center, 1);
                return;
            }
            if (!RollExposure(ctx, target)) { RaiseGuard(target, _settings.GuardOnFailedAttempt); return; }
            Expose(ctx, saboteur, target, rel, s.Id, s.Center,
                "The " + target.Name + " caught " + adj + " saboteurs in " + s.Name + ". Calls for vengeance against the " + saboteur.Name + " rang out.");
        }

        private void SpendOperation(SimContext ctx, Civilization civ, float network, float cost)
        {
            civ.Memory[EspionageKeys.Network] = MathUtil.Clamp(MathUtil.Sanitize(network - cost), 0f, Math.Max(0f, _settings.MaxNetwork));
            civ.Memory[EspionageKeys.LastOpTick] = ctx.State.Tick;
            Bump(civ, EspionageKeys.Operations);
        }

        private bool RollExposure(SimContext ctx, Civilization target)
        {
            float guard = ReadGuard(target);
            float xeno = AdvisorBoard.Trait(target, ctx.Db, AITrait.Xenophobia);
            float hi = _settings.ExposureMax, lo = Math.Min(_settings.ExposureMin, hi);
            float p = MathUtil.Clamp(MathUtil.Sanitize(_settings.ExposureBase + 0.3f * guard + 0.2f * xeno), lo, hi);
            return ctx.Rng.Chance(p);
        }

        private void Expose(SimContext ctx, Civilization spy, Civilization victim, Relation rel, int settlementId, Vec2i pos, string text)
        {
            float lo = Math.Min(_settings.OpinionPenaltyMin, _settings.OpinionPenaltyMax), hi = Math.Max(_settings.OpinionPenaltyMin, _settings.OpinionPenaltyMax);
            float penalty = ctx.Rng.Range(lo, hi);
            if (rel != null) rel.Opinion = MathUtil.Clamp(MathUtil.Sanitize(rel.Opinion - penalty), -100f, 100f);
            victim.Memory[EspionageKeys.ExposedBy(spy.Id)] = ctx.State.Tick;     // casus belli flag
            RaiseGuard(victim, _settings.GuardOnExposure);
            float network = ReadNetwork(spy);
            spy.Memory[EspionageKeys.Network] = MathUtil.Clamp(MathUtil.Sanitize(network * (1f - MathUtil.Clamp01(_settings.NetworkLossOnExposure))), 0f, Math.Max(0f, _settings.MaxNetwork));
            Bump(spy, EspionageKeys.TimesExposed);
            ctx.Ops?.AddChronicle(ChronicleCategory.Diplomacy, text, victim.Id, settlementId, pos, 1);
        }

        // =====================================================================================================
        // public helpers (also used by tests / other AI via composition)
        // =====================================================================================================
        /// <summary>
        /// Best tech <paramref name="thief"/> could steal from <paramref name="target"/>: known by the target, researchable
        /// by the thief (<see cref="GameDatabase.CanResearch"/>), closest to the thief's current age, then cheapest, then lowest id.
        /// Returns -1 if none.
        /// </summary>
        public static int SelectTechToSteal(GameDatabase db, Civilization thief, Civilization target)
        {
            if (db == null || thief == null || target == null) return -1;
            var techs = db.Techs;
            int best = -1, bestRank = int.MaxValue;
            float bestCost = float.MaxValue;
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                if (t == null || !target.KnownTechs.Contains(t.Id) || !db.CanResearch(thief, t)) continue;
                int rank = Math.Abs((int)thief.Age - (int)t.Age);
                float cost = MathUtil.Sanitize(t.Cost, float.MaxValue);
                if (best < 0 || rank < bestRank || (rank == bestRank && cost < bestCost))
                {
                    best = t.Id; bestRank = rank; bestCost = cost;
                }
            }
            return best;
        }

        /// <summary>
        /// Adds a tech to a civ as if it had been researched: KnownTechs, UnlockBuilding effects, prog.dirty = 1 and a
        /// <see cref="TechResearchedEvent"/>. Returns false if the tech is unknown or already known.
        /// </summary>
        public static bool LearnTech(SimContext ctx, Civilization civ, int techId)
        {
            if (ctx == null || ctx.Db == null || civ == null) return false;
            var tech = ctx.Db.Tech(techId);
            if (tech == null || !civ.KnownTechs.Add(tech.Id)) return false;
            var fx = tech.Effects;
            if (fx != null)
                for (int i = 0; i < fx.Length; i++)
                {
                    if (fx[i].Kind != TechEffectKind.UnlockBuilding) continue;
                    int b = (int)fx[i].Building;
                    if (b >= 0 && b < civ.UnlockedBuildings.Length) civ.UnlockedBuildings[b] = true;
                }
            civ.Memory[EspionageKeys.ProgressionDirty] = 1f;
            if (civ.CurrentResearchTechId == tech.Id) civ.CurrentResearchTechId = -1;   // let the research AI repick
            ctx.Events?.Publish(new TechResearchedEvent { CivilizationId = civ.Id, TechId = tech.Id });
            return true;
        }

        /// <summary>Defense points: base + Administration coverage + Xenophobia + recent guard.</summary>
        public float CounterIntelligence(WorldState state, GameDatabase db, Civilization target)
        {
            if (target == null) return _settings.BaseCounterIntel;
            float admin = 0f;
            int n = 0;
            if (state != null)
                for (int i = 0; i < target.SettlementIds.Count; i++)
                {
                    var s = state.Settlements.Get(target.SettlementIds[i]);
                    if (s == null || !s.IsAlive) continue;
                    admin += MathUtil.Clamp(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Administration]), 0f, 1.5f);
                    n++;
                }
            if (n > 0) admin /= n;
            float xeno = AdvisorBoard.Trait(target, db, AITrait.Xenophobia);
            float d = _settings.BaseCounterIntel + _settings.AdministrationWeight * admin + _settings.XenophobiaWeight * xeno + _settings.GuardWeight * ReadGuard(target);
            return Math.Max(0f, MathUtil.Sanitize(d));
        }

        /// <summary>offense / (offense + defense), clamped to [MinSuccessChance, MaxSuccessChance].</summary>
        public float SuccessChance(float offense, float defense)
        {
            offense = Math.Max(0f, MathUtil.Sanitize(offense));
            defense = Math.Max(0f, MathUtil.Sanitize(defense));
            float denom = offense + defense;
            float raw = denom <= MathUtil.Epsilon ? 0.5f : offense / denom;
            float hi = _settings.MaxSuccessChance, lo = Math.Min(_settings.MinSuccessChance, hi);
            return MathUtil.Clamp(MathUtil.Sanitize(raw), lo, hi);
        }

        // =====================================================================================================
        // internals
        // =====================================================================================================
        private static bool IsMoreAdvanced(Civilization other, Civilization civ) => other.Age > civ.Age || other.KnownTechs.Count > civ.KnownTechs.Count;

        private static bool IsHostile(Relation rel) => rel.AtWar || rel.Stance <= DiplomaticStance.Hostile;

        private bool CanSabotage(GameDatabase db, Civilization civ, Relation rel)
        {
            if (!IsHostile(rel)) return false;
            float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            return aggression >= (rel.AtWar ? _settings.SabotageMinAggressionAtWar : _settings.SabotageMinAggression);
        }

        /// <summary>Bold/curious civs gamble on worse odds; diplomatic civs hesitate, especially against friends.</summary>
        private float MinAcceptableChance(GameDatabase db, Civilization civ, Relation rel)
        {
            float aggr = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float curio = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            float diplo = AdvisorBoard.Trait(civ, db, AITrait.Diplomacy);
            float m = 0.5f - 0.3f * aggr - 0.2f * curio + 0.25f * diplo;
            if (rel.Stance == DiplomaticStance.Friendly) m += 0.15f;
            if (rel.AtWar) m -= 0.15f;
            return Math.Max(0f, _settings.RiskAversion) * MathUtil.Clamp(m, 0.05f, 0.8f);
        }

        private bool IsLayingLow(Civilization spy, Civilization target, long tick)
        {
            if (!target.Memory.TryGetValue(EspionageKeys.ExposedBy(spy.Id), out float at) || float.IsNaN(at) || float.IsInfinity(at)) return false;
            return tick - (long)at < _settings.LayLowTicks;
        }

        private void DecayGuard(Civilization civ, float years)
        {
            if (!civ.Memory.TryGetValue(EspionageKeys.Guard, out float g)) return;
            g = MathUtil.Clamp01(MathUtil.Sanitize(g)) * (1f - MathUtil.Clamp01(_settings.GuardDecayPerYear * years));
            if (g < 0.01f) civ.Memory.Remove(EspionageKeys.Guard); else civ.Memory[EspionageKeys.Guard] = g;
        }

        private static void RaiseGuard(Civilization civ, float amount)
        {
            if (civ == null) return;
            civ.Memory[EspionageKeys.Guard] = MathUtil.Clamp01(ReadGuard(civ) + MathUtil.Sanitize(amount));
        }

        private static float ReadGuard(Civilization civ) => MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(EspionageKeys.Guard)));

        private float ReadNetwork(Civilization civ) => MathUtil.Clamp(MathUtil.Sanitize(civ.Mem(EspionageKeys.Network)), 0f, Math.Max(0f, _settings.MaxNetwork));

        private static void Bump(Civilization civ, string key) => civ.Memory[key] = MathUtil.Clamp(MathUtil.Sanitize(civ.Mem(key)) + 1f, 0f, 1e6f);

        private static int PopulationOf(WorldState state, Civilization civ)
        {
            int pop = 0;
            for (int i = 0; i < civ.SettlementIds.Count; i++)
            {
                var s = state.Settlements.Get(civ.SettlementIds[i]);
                if (s != null && s.IsAlive) pop += s.Population;
            }
            return Math.Max(pop, civ.Population);
        }

        private static Settlement PickSettlement(WorldState state, Civilization target, Rng rng)
        {
            int alive = 0;
            for (int i = 0; i < target.SettlementIds.Count; i++) { var s = state.Settlements.Get(target.SettlementIds[i]); if (s != null && s.IsAlive) alive++; }
            if (alive == 0) return null;
            int k = rng.Range(0, alive);
            for (int i = 0; i < target.SettlementIds.Count; i++)
            {
                var s = state.Settlements.Get(target.SettlementIds[i]);
                if (s == null || !s.IsAlive) continue;
                if (k-- == 0) return s;
            }
            return null;
        }

        private Building PickSabotageBuilding(WorldState state, GameDatabase db, Settlement s, Rng rng)
        {
            int count = 0;
            for (int i = 0; i < s.BuildingIds.Count; i++) if (IsSabotageable(state.Buildings.Get(s.BuildingIds[i]), db)) count++;
            if (count == 0) return null;
            int k = rng.Range(0, count);
            for (int i = 0; i < s.BuildingIds.Count; i++)
            {
                var b = state.Buildings.Get(s.BuildingIds[i]);
                if (!IsSabotageable(b, db)) continue;
                if (k-- == 0) return b;
            }
            return null;
        }

        private bool IsSabotageable(Building b, GameDatabase db)
        {
            if (b == null || b.IsDestroyed || !b.IsComplete) return false;
            if (b.Condition <= _settings.MinConditionAfterSabotage + 0.05f) return false;
            if (!db.TryGetBuilding(b.Type, out var def)) return false;
            return !def.IsWonder && !def.UniquePerCivilization && def.Category != BuildingCategory.Wonder;   // never wonders
        }

        private static bool HasStockedGranary(WorldState state, Settlement s)
        {
            if (s.Stockpile.Get(ResourceType.Food) < 1f) return false;
            for (int i = 0; i < s.BuildingIds.Count; i++)
            {
                var b = state.Buildings.Get(s.BuildingIds[i]);
                if (b != null && !b.IsDestroyed && b.IsComplete && b.Type == BuildingType.Granary) return true;
            }
            return false;
        }

        private static string Adjective(GameDatabase db, RaceId race)
        {
            int i = (int)race;
            var r = i >= 0 && i < db.Races.Count ? db.Races[i] : null;
            return r != null && !string.IsNullOrEmpty(r.Adjective) ? r.Adjective : race.ToString();
        }

        /// <summary>"Humans" (race plural) for foreigners; the civ's own name when spying on kin.</summary>
        private static string Victims(GameDatabase db, Civilization thief, Civilization target)
        {
            if (thief.Race == target.Race) return target.Name;
            int i = (int)target.Race;
            var r = i >= 0 && i < db.Races.Count ? db.Races[i] : null;
            return r != null && !string.IsNullOrEmpty(r.PluralName) ? r.PluralName : target.Name;
        }

        private static string BuildingName(GameDatabase db, BuildingType t) => db.TryGetBuilding(t, out var d) && !string.IsNullOrEmpty(d.Name) ? d.Name : t.ToString();
    }
}
