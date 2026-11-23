using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Political life of civilizations. Once a year (TickInterval 96) each civ's government:
    /// <list type="bullet">
    /// <item>adapts its policies: taxes (raised when broke or at war, lowered as unrest rises), conscription
    /// (raised in war) and faith policy (from regime and piety);</item>
    /// <item>applies them directly and boundedly to every settlement: taxes add gold (population x tax x DeltaTicks)
    /// and cost a little happiness, conscription votes Soldier jobs (and barracks in war), faith policy votes faith
    /// buildings, all through the <see cref="AdvisorBoard"/>;</item>
    /// <item>updates its stability (happiness, unrest, famine, wars, taxes, legitimacy, age-appropriateness);</item>
    /// <item>may change: peaceful reforms on age advances, personality drift or crises, and revolutions when stability
    /// collapses (chronicle importance 2).</item>
    /// </list>
    /// State lives in civ memory ("gov.*", see <see cref="GovernanceKeys"/>). Deterministic; allocation-free except on
    /// the rare government change (chronicle text).
    /// </summary>
    public sealed class GovernanceSystem : AdvisorSystemBase
    {
        /// <summary>Advisor id ("gov").</summary>
        public const string Id = GovernanceKeys.AdvisorId;

        public override string AdvisorId => Id;
        public override string Name => "AI.Governance";
        public override int TickInterval => 96;

        private static readonly BuildingType[] s_faithBuildings =
        {
            BuildingType.Cathedral, BuildingType.Monastery, BuildingType.Temple, BuildingType.Shrine, BuildingType.SpiritTotem
        };
        private static readonly BuildingType[] s_militaryBuildings =
        {
            BuildingType.MilitaryBase, BuildingType.Castle, BuildingType.Barracks
        };

        private readonly List<int> _processed = new List<int>();
        private long _runTick = long.MinValue;

        public override void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (civs[i].IsAlive) EnsureInitialized(ctx, civs[i]);
        }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            BeginRun(ctx);
            if (!_processed.Contains(civ.Id))
            {
                _processed.Add(civ.Id);
                UpdateCivilization(ctx, civ);
            }
            ApplySettlementPolicies(ctx, civ, s);
        }

        protected override void AfterRun(SimContext ctx)
        {
            BeginRun(ctx);
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (civs[i].IsAlive && !_processed.Contains(civs[i].Id)) EnsureInitialized(ctx, civs[i]);
            _processed.Clear();
            _runTick = long.MinValue;
        }

        private void BeginRun(SimContext ctx)
        {
            if (_runTick == ctx.Tick) return;
            _runTick = ctx.Tick;
            _processed.Clear();
        }

        // ------------------------------------------------------------------ civ level
        /// <summary>Gives a civ its first government (Tribal Council in the early ages) and default policies.</summary>
        public static void EnsureInitialized(SimContext ctx, Civilization civ)
        {
            if (civ == null || civ.Memory.ContainsKey(GovernanceKeys.Type)) return;
            var type = GovernmentType.TribalCouncil;
            if (!GovernmentCatalog.Get(type).IsEligible(civ.Age))
            {
                var snap = Gather(ctx, civ, Math.Max(1, ctx.DeltaTicks), false);
                type = GovernanceModel.Best(civ, ctx.Db, snap, type, -1, 0f, out _);
            }
            var def = GovernmentCatalog.Get(type);
            civ.Memory[GovernanceKeys.Type] = (float)(int)type;
            civ.Memory[GovernanceKeys.Since] = ctx.Tick;
            civ.Memory[GovernanceKeys.Stability] = 0.7f;
            civ.Memory[GovernanceKeys.Tax] = def.BaseTax;
            civ.Memory[GovernanceKeys.Conscription] = def.BaseConscription;
            civ.Memory[GovernanceKeys.FaithPolicy] = (float)(int)GovernanceModel.FaithTarget(def, civ, ctx.Db);
            civ.Memory[GovernanceKeys.SeenAge] = (float)(int)civ.Age;
            ApplyAdvisorBias(civ, null, def);
        }

        private void UpdateCivilization(SimContext ctx, Civilization civ)
        {
            EnsureInitialized(ctx, civ);
            int dt = Math.Max(1, ctx.DeltaTicks);
            var snap = Gather(ctx, civ, dt, true);
            civ.Memory[GovernanceKeys.AtWar] = snap.AtWar ? 1f : 0f;

            var type = GovernanceQueries.Government(civ);
            var def = GovernmentCatalog.Get(type);
            float years = GovernanceQueries.YearsInPower(civ, ctx.Tick);
            float yearFrac = dt / (float)SimTime.TicksPerYear;

            // ---- policies adapt to needs
            float tax = GovernanceQueries.Tax(civ);
            float consc = GovernanceQueries.Conscription(civ);
            tax += (GovernanceModel.TaxTarget(def, snap) - tax) * Rate(0.4f, yearFrac);
            consc += (GovernanceModel.ConscriptionTarget(def, civ, ctx.Db, snap) - consc) * Rate(0.6f, yearFrac);
            tax = MathUtil.Clamp01(MathUtil.Sanitize(tax, def.BaseTax));
            consc = MathUtil.Clamp01(MathUtil.Sanitize(consc, def.BaseConscription));
            civ.Memory[GovernanceKeys.Tax] = tax;
            civ.Memory[GovernanceKeys.Conscription] = consc;
            var faith = GovernanceModel.FaithTarget(def, civ, ctx.Db);
            if (faith != GovernanceQueries.Faith(civ))
            {
                civ.Memory[GovernanceKeys.FaithPolicy] = (float)(int)faith;
                if (faith == FaithPolicy.Zealotry || faith == FaithPolicy.Secular)
                {
                    string place = PlaceName(ctx, civ);
                    ctx.Ops.AddChronicle(ChronicleCategory.Civilization, faith == FaithPolicy.Zealotry
                        ? "The " + def.StateName + " of " + place + " made the faith the law of the land."
                        : "The " + def.StateName + " of " + place + " set the temples apart from the state.", civ.Id, civ.CapitalId, CapitalPos(ctx, civ), 1);
                }
            }

            // ---- stability
            float stability = GovernanceQueries.Stability(civ);
            float target = GovernanceModel.StabilityTarget(def, civ, snap, tax, consc, years);
            stability += (target - stability) * Rate(0.35f, yearFrac);
            stability -= MathUtil.Clamp01(snap.FamineShare) * 0.08f;
            if (snap.LostWar) stability -= 0.12f;
            stability = MathUtil.Clamp01(MathUtil.Sanitize(stability, 0.5f));
            civ.Memory[GovernanceKeys.Stability] = stability;
            civ.Memory[GovernanceKeys.StabilityTarget] = target;

            // ---- transitions
            float crisis = snap.Crisis;
            bool canFall = years >= 1f || ctx.Tick - (long)civ.Mem(GovernanceKeys.Since) >= dt;
            if (canFall && (stability < GovernanceModel.RevolutionThreshold ||
                (stability < GovernanceModel.UnrestThreshold && ctx.Rng.Chance(Rng.PerYearToPerTicks(GovernanceModel.RevolutionChancePerYear, dt)))))
            {
                Revolution(ctx, civ, snap, type, crisis);
                return;
            }

            bool outdated = !def.IsEligible(civ.Age);
            bool crisisMode = stability < 0.35f && crisis > 0.3f && years >= 3f;
            bool periodic = years >= 12f;
            if (!(snap.AgeAdvanced || outdated || crisisMode || periodic)) return;

            var best = GovernanceModel.Best(civ, ctx.Db, snap, type, -1, crisisMode ? crisis : 0f, out float bestScore);
            if (best == type) return;
            float current = GovernanceModel.Score(def, civ, ctx.Db, snap, true, crisisMode ? crisis : 0f);
            float margin = snap.AgeAdvanced ? GovernanceModel.AgeReformMargin : (crisisMode ? GovernanceModel.CrisisReformMargin : GovernanceModel.ReformMargin);
            if (outdated || bestScore - current >= margin) Reform(ctx, civ, snap, type, best, crisisMode);
        }

        private static float Rate(float perYear, float yearFrac) =>
            MathUtil.Clamp01(1f - (float)Math.Pow(1.0 - perYear, Math.Max(0f, yearFrac)));

        /// <summary>Collects civ-wide political conditions (no allocations).</summary>
        public static GovernanceSnapshot Gather(SimContext ctx, Civilization civ, int deltaTicks, bool consumeAgeChange)
        {
            var snap = new GovernanceSnapshot();
            if (ctx == null || civ == null) return snap;
            long since = ctx.Tick - Math.Max(1, deltaTicks);
            float popW = 0f, happy = 0f, unrest = 0f, famine = 0f;
            int alive = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = ctx.State.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive) continue;
                alive++;
                int pop = s.Population;
                float w = Math.Max(1, pop);
                popW += w;
                snap.Population += pop;
                happy += MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.5f)) * w;
                unrest += MathUtil.Clamp01(MathUtil.Sanitize(s.Unrest)) * w;
                if (s.LastStarvationTick >= 0 && s.LastStarvationTick > since) famine += w;
                snap.Gold += MathUtil.Sanitize(s.Stockpile.Get(ResourceType.Gold));
                snap.GoldNet += MathUtil.Sanitize(s.Stockpile.Net(ResourceType.Gold));
            }
            snap.Settlements = alive;
            snap.AvgHappiness = popW > 0f ? happy / popW : 0.5f;
            snap.AvgUnrest = popW > 0f ? unrest / popW : 0f;
            snap.FamineShare = popW > 0f ? famine / popW : 0f;

            var wars = ctx.State.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || (w.AttackerId != civ.Id && w.DefenderId != civ.Id)) continue;
                float score = MathUtil.Sanitize(w.WarScore);
                if (w.DefenderId == civ.Id) score = -score;
                if (w.IsActive)
                {
                    snap.AtWar = true;
                    if (score <= -30f) snap.LosingWar = true;
                }
                else if (w.EndTick > since)
                {
                    if (score <= -20f) snap.LostWar = true;
                    else if (score >= 20f) snap.WonWar = true;
                }
            }

            float seen = MathUtil.Sanitize(civ.Mem(GovernanceKeys.SeenAge, (float)(int)civ.Age), (float)(int)civ.Age);
            snap.AgeAdvanced = (int)civ.Age > MathUtil.RoundToInt(seen);
            if (consumeAgeChange) civ.Memory[GovernanceKeys.SeenAge] = (float)(int)civ.Age;
            return snap;
        }

        private static void Revolution(SimContext ctx, Civilization civ, in GovernanceSnapshot snap, GovernmentType oldType, float crisis)
        {
            var newType = GovernanceModel.Best(civ, ctx.Db, snap, oldType, (int)oldType, Math.Max(crisis, 0.5f), out _);
            var oldDef = GovernmentCatalog.Get(oldType);
            string place = PlaceName(ctx, civ);
            if (newType == oldType)
            {
                // No alternative exists in this age: the uprising is crushed, at a cost.
                civ.Memory[GovernanceKeys.Stability] = 0.3f;
                civ.Memory[GovernanceKeys.Since] = ctx.Tick;
                ctx.Ops.AddChronicle(ChronicleCategory.Civilization, "An uprising against the " + oldDef.RulerTitle + " of " + place + " was crushed.",
                    civ.Id, civ.CapitalId, CapitalPos(ctx, civ), 1);
                return;
            }
            var newDef = GovernmentCatalog.Get(newType);
            SetGovernment(ctx, civ, oldDef, newDef, GovernanceModel.PostRevolutionStability);
            // A new regime promises relief: taxes fall to its baseline at most.
            civ.Memory[GovernanceKeys.Tax] = Math.Min(GovernanceQueries.Tax(civ), newDef.BaseTax);
            civ.Memory[GovernanceKeys.Revolutions] = civ.Mem(GovernanceKeys.Revolutions) + 1f;
            string text = newDef.Faction + " overthrew the " + oldDef.RulerTitle + " of " + place + "; the " + newDef.StateName + " of " + place + " was proclaimed.";
            ctx.Ops.AddChronicle(ChronicleCategory.Civilization, text, civ.Id, civ.CapitalId, CapitalPos(ctx, civ), 2);
            LogCapital(ctx, civ, "[gov] revolution: " + oldDef.Name + " -> " + newDef.Name);
        }

        private static void Reform(SimContext ctx, Civilization civ, in GovernanceSnapshot snap, GovernmentType oldType, GovernmentType newType, bool crisisMode)
        {
            var oldDef = GovernmentCatalog.Get(oldType);
            var newDef = GovernmentCatalog.Get(newType);
            string reason = ReasonFor(ctx, civ, snap, newDef, crisisMode);
            float stability = GovernanceQueries.Stability(civ);
            SetGovernment(ctx, civ, oldDef, newDef, MathUtil.Lerp(stability, GovernanceModel.PostReformStability, 0.5f));
            civ.Memory[GovernanceKeys.Reforms] = civ.Mem(GovernanceKeys.Reforms) + 1f;
            string place = PlaceName(ctx, civ);
            string text = "The " + oldDef.StateName + " of " + place + " became the " + newDef.StateName + " of " + place + ", " + reason + ".";
            ctx.Ops.AddChronicle(ChronicleCategory.Civilization, text, civ.Id, civ.CapitalId, CapitalPos(ctx, civ), 1);
            LogCapital(ctx, civ, "[gov] reform: " + oldDef.Name + " -> " + newDef.Name);
        }

        private static string ReasonFor(SimContext ctx, Civilization civ, in GovernanceSnapshot snap, GovernmentDefinition def, bool crisisMode)
        {
            var pulls = def.TraitPulls;
            if (pulls != null)
                for (int i = 0; i < pulls.Length; i++)
                    if (pulls[i].Weight >= 0.8f && AdvisorBoard.Trait(civ, ctx.Db, pulls[i].Trait) > 0.65f) return def.ReformReason;
            if (crisisMode)
            {
                if (snap.LostWar || snap.LosingWar) return "humbled by defeat in war";
                if (snap.FamineShare > 0.2f) return "as famine stalked the land";
                return "amid unrest in the streets";
            }
            if (snap.AgeAdvanced || !def.IsEligible(civ.Age)) return "as the " + AgeName(ctx, civ.Age) + " age dawned";
            return def.ReformReason;
        }

        private static void SetGovernment(SimContext ctx, Civilization civ, GovernmentDefinition oldDef, GovernmentDefinition newDef, float stability)
        {
            civ.Memory[GovernanceKeys.Type] = (float)(int)newDef.Type;
            civ.Memory[GovernanceKeys.Since] = ctx.Tick;
            civ.Memory[GovernanceKeys.Stability] = MathUtil.Clamp01(stability);
            civ.Memory[GovernanceKeys.FaithPolicy] = (float)(int)GovernanceModel.FaithTarget(newDef, civ, ctx.Db);
            ApplyAdvisorBias(civ, oldDef, newDef);
        }

        /// <summary>Undoes the old regime's advisor weight multipliers and applies the new one's (weights stay in [0, 3]).</summary>
        public static void ApplyAdvisorBias(Civilization civ, GovernmentDefinition oldDef, GovernmentDefinition newDef)
        {
            if (civ == null) return;
            if (oldDef != null && oldDef.AdvisorBiases != null)
                for (int i = 0; i < oldDef.AdvisorBiases.Length; i++)
                {
                    var b = oldDef.AdvisorBiases[i];
                    if (b.Multiplier > 0.01f) AdvisorBoard.SetWeight(civ, b.AdvisorId, AdvisorBoard.Weight(civ, b.AdvisorId) / b.Multiplier);
                }
            if (newDef != null && newDef.AdvisorBiases != null)
                for (int i = 0; i < newDef.AdvisorBiases.Length; i++)
                {
                    var b = newDef.AdvisorBiases[i];
                    AdvisorBoard.SetWeight(civ, b.AdvisorId, AdvisorBoard.Weight(civ, b.AdvisorId) * b.Multiplier);
                }
        }

        // ------------------------------------------------------------------ settlement level
        private void ApplySettlementPolicies(SimContext ctx, Civilization civ, Settlement s)
        {
            var def = GovernmentCatalog.Get(GovernanceQueries.Government(civ));
            int dt = Math.Max(1, ctx.DeltaTicks);
            float tax = GovernanceQueries.Tax(civ);

            // Taxes: gold from the people, at a small, bounded happiness cost.
            float gold = GovernanceModel.TaxGold(def, s.Population, tax, dt, civ.Modifiers.Trade);
            if (gold > 0f) s.Stockpile.Add(ResourceType.Gold, gold);
            float h = MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.5f));
            if (h > GovernanceModel.HappinessFloor)
                s.Happiness = Math.Max(GovernanceModel.HappinessFloor, h - GovernanceModel.TaxHappinessCost(def, tax, dt));

            // Conscription: staff soldiers; in war also ask for barracks if the levy has nowhere to train.
            float consc = GovernanceQueries.Conscription(civ);
            if (consc >= 0.08f)
            {
                VoteJob(s, civ, JobType.Soldier, MathUtil.Clamp(consc * def.Militarism * 1.5f, 0f, 1f));
                if (civ.Mem(GovernanceKeys.AtWar) > 0.5f && s.Soldiers < s.Population * consc * 0.5f)
                {
                    var mil = FirstAvailable(ctx, civ, s_militaryBuildings);
                    if (mil.HasValue) VoteBuild(s, civ, mil.Value, MathUtil.Clamp(consc * 0.8f, 0f, 1f));
                }
            }

            // Faith policy: fund (or starve) the temples.
            var faith = GovernanceQueries.Faith(civ);
            float fv = GovernanceModel.FaithVote(faith, def.FaithZeal);
            var temple = FirstAvailable(ctx, civ, s_faithBuildings);
            if (temple.HasValue && fv != 0f) VoteBuild(s, civ, temple.Value, fv);
            if (faith >= FaithPolicy.StateFaith) VoteJob(s, civ, JobType.Priest, MathUtil.Clamp(fv * 0.5f, 0f, 1f));

            Urgency(s, 1f - GovernanceQueries.Stability(civ));
        }

        private static BuildingType? FirstAvailable(SimContext ctx, Civilization civ, BuildingType[] options)
        {
            if (ctx.Db == null) return null;
            for (int i = 0; i < options.Length; i++)
                if (ctx.Db.IsBuildingAvailable(civ, options[i])) return options[i];
            return null;
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Name used in political chronicle lines: the capital's name, else the civ name.</summary>
        public static string PlaceName(SimContext ctx, Civilization civ)
        {
            if (civ == null) return "";
            var cap = civ.CapitalId >= 0 && ctx != null ? ctx.State.Settlements.Get(civ.CapitalId) : null;
            if (cap == null && ctx != null && civ.SettlementIds.Count > 0) cap = ctx.State.Settlements.Get(civ.SettlementIds[0]);
            return cap != null && !string.IsNullOrEmpty(cap.Name) ? cap.Name : civ.Name;
        }

        private static Vec2i CapitalPos(SimContext ctx, Civilization civ)
        {
            var cap = civ.CapitalId >= 0 ? ctx.State.Settlements.Get(civ.CapitalId) : null;
            return cap != null ? cap.Center : Vec2i.Invalid;
        }

        private static void LogCapital(SimContext ctx, Civilization civ, string line)
        {
            var cap = civ.CapitalId >= 0 ? ctx.State.Settlements.Get(civ.CapitalId) : null;
            if (cap != null && cap.IsAlive) cap.LogAi(line);
        }

        private static string AgeName(SimContext ctx, AgeId age)
        {
            var ages = ctx.Db != null ? ctx.Db.Ages : null;
            var a = ages != null && (int)age < ages.Count ? ages[(int)age] : null;
            return a != null && !string.IsNullOrEmpty(a.Name) ? a.Name : age.ToString();
        }
    }
}
