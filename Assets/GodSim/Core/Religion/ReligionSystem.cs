using System;
using System.Collections.Generic;

namespace GodSim.Core.Religion
{
    /// <summary>
    /// Mortal religions that interpret the player-god. Runs every 48 ticks and, per living civilization:
    /// <list type="number">
    /// <item>Founds a faith once the civ has a completed Spirit Totem / Shrine / Temple (or any greater religious
    /// building) or Devotion &gt; 0.6, picking one doctrine per axis from race personality and the god's history.</item>
    /// <item>Keeps a holy site (a settlement owning a Sacred tile, else the host of the highest-faith building).</item>
    /// <item>Applies tiny doctrine effects: settlement/resident Faith, Happiness and Unrest nudges, a Devotion
    /// response to the god's favour, research (Monasticism, Celestial Order) and a defense bonus (Martial Zeal).</item>
    /// <item>Grows or heals heresy; above 0.7 a schism strikes a non-capital settlement (+0.4 unrest) and a
    /// Reformation two years later replaces one doctrine.</item>
    /// <item>Lets fervent neighbours exert conversion pressure (heresy, occasional doctrine swap).</item>
    /// <item>Publishes a holy war target for Martial Zeal faiths facing hostile, opposed-view neighbours.</item>
    /// </list>
    /// <para>All state lives in <c>Civilization.Memory</c> / <c>Settlement.Memory</c> (see <see cref="ReligionKeys"/>);
    /// the system itself holds only scratch buffers, so a new instance resumes seamlessly.</para>
    /// <para>Coexistence with the Divine module's FaithSystem (which drifts Devotion, decays GodFavor and computes
    /// mana): this system never writes GodFavor or mana, and every summed Faith/Happiness/Unrest/Devotion nudge is
    /// clamped to <see cref="ReligionTuning.MaxNudgePerRun"/> (0.02) per run. The only large change is the
    /// schism's one-off +0.4 unrest.</para>
    /// </summary>
    public sealed class ReligionSystem : ISimSystem
    {
        private const int AxisWorship = (int)DoctrineAxis.Worship;
        private const int AxisEthos = (int)DoctrineAxis.Ethos;
        private const int AxisPractice = (int)DoctrineAxis.Practice;
        private const int AxisView = (int)DoctrineAxis.View;

        private readonly List<float> _avgFaith = new List<float>();
        private readonly List<int> _scratchIds = new List<int>();
        private readonly List<int> _axes = new List<int>(DoctrineCatalog.AxisCount);
        private readonly float[] _features = new float[DoctrineSelector.TraitCount];
        private readonly DoctrineDefinition[] _docs = new DoctrineDefinition[DoctrineCatalog.AxisCount];
        private BuildingType _lastHolyBuilding = BuildingType.Shrine;

        /// <summary>Creates the system (stateless apart from scratch buffers).</summary>
        public ReligionSystem() { }

        /// <inheritdoc />
        public string Name => "Religion";

        /// <inheritdoc />
        public int TickInterval => ReligionTuning.TickInterval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) if (civs[i] != null) SanitizeMemory(civs[i]);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var state = ctx.State;
            var civs = state.Civilizations.Items;
            if (civs.Count == 0) return;
            int delta = ctx.DeltaTicks < 1 ? 1 : ctx.DeltaTicks;
            float scale = MathUtil.Clamp(delta / (float)ReligionTuning.TickInterval, 0f, 1f);

            // Snapshot every civ's average faith first so conversion pressure does not depend on iteration order.
            _avgFaith.Clear();
            for (int i = 0; i < civs.Count; i++) _avgFaith.Add(AverageFaith(state, civs[i]));

            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                SanitizeMemory(civ);
                if (!ReligionQueries.HasFaith(civ))
                {
                    TryFound(ctx, civ);
                    continue; // effects start on the next run
                }
                RunFaith(ctx, civ, i, delta, scale);
            }
        }

        // =====================================================================================================
        // founding & doctrines
        // =====================================================================================================

        /// <summary>Founds the civ's faith when its conditions hold. Returns true if a faith was founded.</summary>
        private bool TryFound(SimContext ctx, Civilization civ)
        {
            var state = ctx.State;
            Settlement seat = FaithBuildingSettlement(state, civ);
            float devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f));
            if (seat == null && !(devotion > ReligionTuning.FoundingDevotion)) return false;
            if (seat == null) seat = PreferredSettlement(state, civ);
            if (seat == null) return false; // no living settlement: nowhere for prophets to preach

            var mem = civ.Memory;
            mem[ReligionKeys.Founded] = 1f;
            mem[ReligionKeys.FoundedTick] = state.Tick;
            mem[ReligionKeys.Heresy] = 0f;
            mem[ReligionKeys.HolySite] = -1f;
            mem[ReligionKeys.HolySiteSacred] = 0f;
            mem[ReligionKeys.SeenCasts] = Math.Max(0, state.God.PowersCast);
            bool holyChanged = UpdateHolySite(ctx, civ);
            bool sacred = civ.Mem(ReligionKeys.HolySiteSacred) > 0.5f;

            DoctrineSelector.Features(ReligionQueries.RaceOf(ctx.Db, civ), state.God, sacred, _features);
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
            {
                var id = DoctrineSelector.Select((DoctrineAxis)a, _features, state.Seed, civ.Id);
                mem[DoctrineCatalog.MemoryKey((DoctrineAxis)a)] = (float)id;
            }
            LoadDoctrines(civ);

            string faith = FaithNameOf(ctx, civ);
            string text = "In " + seat.Name + ", prophets of the " + civ.Name + " proclaimed " + faith + ": the god is "
                + Tenet(AxisView) + "; the faithful honour " + Tenet(AxisWorship) + " through " + Tenet(AxisPractice)
                + " and live by " + Tenet(AxisEthos) + ".";
            Chronicle(ctx, text, civ.Id, seat.Id, seat.Center, 2);
            if (holyChanged) AnnounceHolySite(ctx, civ, faith);
            return true;
        }

        /// <summary>Rewrites any missing/corrupt doctrine id with a fresh deterministic pick (self-healing saves).</summary>
        private void EnsureDoctrines(SimContext ctx, Civilization civ)
        {
            bool featuresReady = false;
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
            {
                if (ReligionQueries.Doctrine(civ, (DoctrineAxis)a) != null) continue;
                if (!featuresReady)
                {
                    DoctrineSelector.Features(ReligionQueries.RaceOf(ctx.Db, civ), ctx.State.God, civ.Mem(ReligionKeys.HolySiteSacred) > 0.5f, _features);
                    featuresReady = true;
                }
                civ.Memory[DoctrineCatalog.MemoryKey((DoctrineAxis)a)] = (float)DoctrineSelector.Select((DoctrineAxis)a, _features, ctx.State.Seed, civ.Id);
            }
        }

        private void LoadDoctrines(Civilization civ)
        {
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++) _docs[a] = ReligionQueries.Doctrine(civ, (DoctrineAxis)a);
        }

        private string Tenet(int axis) => _docs[axis] != null ? _docs[axis].Tenet : "the unknown";

        // =====================================================================================================
        // per-run faith life
        // =====================================================================================================

        private void RunFaith(SimContext ctx, Civilization civ, int civIndex, int delta, float scale)
        {
            var state = ctx.State;
            EnsureDoctrines(ctx, civ);
            LoadDoctrines(civ);

            if (UpdateHolySite(ctx, civ)) AnnounceHolySite(ctx, civ, FaithNameOf(ctx, civ));
            ApplyEffects(ctx, civ, delta, scale);

            float pressure = ConversionPressure(state, civ, civIndex, out int influencer);
            SetOrRemove(civ.Memory, ReligionKeys.ConversionPressure, pressure);
            UpdateHeresy(ctx, civ, delta, pressure);
            TryReformation(ctx, civ);
            TrySchism(ctx, civ);
            TryConversion(ctx, civ, influencer, pressure, delta);
            UpdateHolyWar(ctx, civ);
            civ.Memory[ReligionKeys.SeenCasts] = Math.Max(0, state.God.PowersCast);
        }

        // ----------------------------------------------------------------------------------------- holy site

        /// <summary>
        /// Keeps the holy site valid. Sacred-tile settlements win (most Sacred tiles); otherwise an existing site is
        /// kept; otherwise the host of the highest-faith building is chosen. Returns true when the site changed.
        /// </summary>
        private bool UpdateHolySite(SimContext ctx, Civilization civ)
        {
            var state = ctx.State;
            var map = state.Map;
            int current = ReligionQueries.HolySite(civ);
            bool currentSacred = civ.Mem(ReligionKeys.HolySiteSacred) > 0.5f;
            var cur = OwnSettlement(state, civ, current);
            if (cur != null && currentSacred && CountSacred(map, cur) > 0) return false;

            Settlement bestSacred = null, bestShrine = null;
            int bestSacredCount = 0;
            float bestShrineScore = 0f;
            BuildingType bestType = BuildingType.Shrine;
            var ids = civ.SettlementIds;
            for (int j = 0; j < ids.Count; j++)
            {
                var s = OwnSettlement(state, civ, ids[j]);
                if (s == null) continue;
                int sacred = CountSacred(map, s);
                if (sacred > bestSacredCount) { bestSacredCount = sacred; bestSacred = s; }
                float score = BestFaithBuilding(ctx.Db, state, s, out BuildingType type);
                if (score > bestShrineScore) { bestShrineScore = score; bestShrine = s; bestType = type; }
            }

            Settlement chosen;
            bool chosenSacred;
            if (bestSacred != null) { chosen = bestSacred; chosenSacred = true; }
            else if (cur != null) { civ.Memory[ReligionKeys.HolySiteSacred] = 0f; return false; }
            else if (bestShrine != null) { chosen = bestShrine; chosenSacred = false; }
            else
            {
                civ.Memory[ReligionKeys.HolySite] = -1f;
                civ.Memory[ReligionKeys.HolySiteSacred] = 0f;
                return false;
            }

            civ.Memory[ReligionKeys.HolySiteSacred] = chosenSacred ? 1f : 0f;
            if (cur != null && chosen.Id == cur.Id) return false;
            civ.Memory[ReligionKeys.HolySite] = chosen.Id;
            if (!chosenSacred) _lastHolyBuilding = bestType;
            else BestFaithBuilding(ctx.Db, state, chosen, out _lastHolyBuilding);
            return true;
        }

        private void AnnounceHolySite(SimContext ctx, Civilization civ, string faith)
        {
            var site = OwnSettlement(ctx.State, civ, ReligionQueries.HolySite(civ));
            if (site == null) return;
            bool sacred = civ.Mem(ReligionKeys.HolySiteSacred) > 0.5f;
            string text;
            if (sacred)
                text = faith + " consecrated " + site.Name + " as its holy site, where the sacred ground hums with the god's presence.";
            else
            {
                string building = ctx.Db != null && ctx.Db.TryGetBuilding(_lastHolyBuilding, out var def) && !string.IsNullOrEmpty(def.Name) ? def.Name : _lastHolyBuilding.ToString();
                text = faith + " consecrated " + site.Name + " as its holy site; pilgrims gather in the shadow of its " + building + ".";
            }
            Chronicle(ctx, text, civ.Id, site.Id, site.Center, sacred ? 2 : 1);
        }

        /// <summary>Number of Sacred tiles owned by the settlement (scanned within its territory radius + margin).</summary>
        public static int CountSacred(WorldMap map, Settlement s)
        {
            if (map == null || s == null) return 0;
            int r = MathUtil.Clamp(s.TerritoryRadius + ReligionTuning.SacredScanMargin, 1, 64);
            int x0 = Math.Max(0, s.Center.X - r), x1 = Math.Min(map.Width - 1, s.Center.X + r);
            int y0 = Math.Max(0, s.Center.Y - r), y1 = Math.Min(map.Height - 1, s.Center.Y + r);
            int n = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = map.Index(x, y);
                    if (map.Owner[i] == s.Id && (map.Flags[i] & TileFlags.Sacred) != 0) n++;
                }
            return n;
        }

        /// <summary>Rank of a religious building (0 = not religious). Spirit Totem 1 .. faith wonders 5.</summary>
        public static int FaithBuildingRank(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.SpiritTotem: return 1;
                case BuildingType.Shrine: return 2;
                case BuildingType.Temple: return 3;
                case BuildingType.Monastery: return 3;
                case BuildingType.Cathedral: return 4;
                case BuildingType.WarIdol: return 4;
                case BuildingType.GrandCathedral: return 5;
                case BuildingType.WorldTree: return 5;
                case BuildingType.SunPyramid: return 5;
                default: return 0;
            }
        }

        /// <summary>Score of the settlement's best completed religious building (rank + Faith service amount), 0 = none.</summary>
        private static float BestFaithBuilding(GameDatabase db, WorldState state, Settlement s, out BuildingType type)
        {
            type = BuildingType.Shrine;
            float best = 0f;
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b == null || b.IsDestroyed || !b.IsComplete || b.SettlementId != s.Id) continue;
                int rank = FaithBuildingRank(b.Type);
                float service = 0f;
                if (db != null && db.TryGetBuilding(b.Type, out var def) && def.Services != null)
                    for (int q = 0; q < def.Services.Length; q++)
                        if (def.Services[q].Type == ServiceType.Faith) service += Math.Max(0f, MathUtil.Sanitize(def.Services[q].Amount));
                if (rank <= 0 && service <= 0f) continue;
                float score = rank + MathUtil.Clamp(service, 0f, 100f);
                if (score > best) { best = score; type = b.Type; }
            }
            return best;
        }

        // ----------------------------------------------------------------------------------------- effects

        private void ApplyEffects(SimContext ctx, Civilization civ, int delta, float scale)
        {
            var state = ctx.State;
            float faith = 0f, happy = 0f, unrest = 0f, bless = 0f, wrath = 0f, flat = 0f;
            float resFaith = 0f, resFlat = 0f, defense = 0f, holyFaith = 0f, pilgrim = 0f;
            for (int a = 0; a < _docs.Length; a++)
            {
                var d = _docs[a];
                if (d == null) continue;
                faith += d.FaithNudge; happy += d.HappinessNudge; unrest += d.UnrestNudge;
                bless += d.BlessingDevotion; wrath += d.WrathDevotion; flat += d.FlatDevotion;
                resFaith += d.ResearchPerFaith; resFlat += d.ResearchPerSettlement; defense += d.DefenseBonus;
                holyFaith += d.HolySiteFaith; pilgrim += d.PilgrimageHappiness;
            }

            float heresy = ReligionQueries.Heresy(civ);
            float heresyUnrest = heresy > ReligionTuning.HeresyUnrestThreshold ? (heresy - ReligionTuning.HeresyUnrestThreshold) * ReligionTuning.HeresyUnrestPerRun : 0f;
            int holy = ReligionQueries.HolySite(civ);
            bool hasHoly = OwnSettlement(state, civ, holy) != null;
            float defenseBonus = MathUtil.Clamp(defense, 0f, ReligionTuning.MaxDefenseBonus);

            double faithOutput = 0;
            int living = 0;
            var ids = civ.SettlementIds;
            for (int j = 0; j < ids.Count; j++)
            {
                var s = OwnSettlement(state, civ, ids[j]);
                if (s == null) continue;
                living++;
                if (s.ServiceOutput.Length > (int)ServiceType.Faith)
                    faithOutput += Math.Max(0f, MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Faith]));
                bool isHoly = hasHoly && s.Id == holy;
                float f = faith + (isHoly ? ReligionTuning.HolySiteFaith + holyFaith : 0f);
                float h = happy + (isHoly ? ReligionTuning.HolySiteHappiness : (hasHoly ? ReligionTuning.PilgrimageHappiness + pilgrim : 0f));
                float u = unrest + heresyUnrest;
                NudgeSettlement(state, s, Bound(f) * scale, Bound(h) * scale, Bound(u) * scale);
                ApplyDefense(s, defenseBonus);
            }

            // Devotion: how this faith reads the god's favour (blessings vs wrath). FaithSystem owns the drift.
            float favor = MathUtil.Clamp(MathUtil.Sanitize(civ.GodFavor), -1f, 1f);
            float dev = flat + (favor >= 0f ? bless * favor : wrath * -favor);
            civ.Devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f) + Bound(dev) * scale);

            // Research from cloisters and star-readers.
            float perTick = MathUtil.Clamp(MathUtil.Sanitize(resFaith * (float)faithOutput + resFlat * living), 0f, ReligionTuning.MaxResearchPerTick);
            if (perTick > 0f)
                civ.ResearchPool = MathUtil.Clamp(MathUtil.Sanitize(civ.ResearchPool) + perTick * delta, 0f, ReligionTuning.MaxResearchPool);
            SetOrRemove(civ.Memory, ReligionKeys.ResearchBonus, perTick);
            SetOrRemove(civ.Memory, ReligionKeys.DefenseBonus, defenseBonus);
        }

        private static float Bound(float v) => MathUtil.Clamp(MathUtil.Sanitize(v), -ReligionTuning.MaxNudgePerRun, ReligionTuning.MaxNudgePerRun);

        /// <summary>Nudges settlement aggregates and its residents (the wellbeing module re-averages residents).</summary>
        private static void NudgeSettlement(WorldState state, Settlement s, float faith, float happiness, float unrest)
        {
            if (faith != 0f || happiness != 0f)
            {
                var residents = s.ResidentIds;
                for (int k = 0; k < residents.Count; k++)
                {
                    var p = state.People.Get(residents[k]);
                    if (p == null || !p.IsAlive) continue;
                    p.Faith = MathUtil.Clamp01(MathUtil.Sanitize(p.Faith, 0.5f) + faith);
                    p.Happiness = MathUtil.Clamp01(MathUtil.Sanitize(p.Happiness, 0.5f) + happiness);
                }
            }
            s.Faith = MathUtil.Clamp01(MathUtil.Sanitize(s.Faith, 0.5f) + faith);
            s.Happiness = MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.5f) + happiness);
            s.Unrest = MathUtil.Clamp01(MathUtil.Sanitize(s.Unrest) + unrest);
        }

        /// <summary>
        /// Publishes <c>rel.defenseBonus</c> and bumps a freshly computed DefenseStrength once (never compounds: if the
        /// value is still the one this system wrote, nothing recomputed it and it is left alone).
        /// </summary>
        private static void ApplyDefense(Settlement s, float bonus)
        {
            if (!(bonus > 0f))
            {
                s.Memory.Remove(ReligionKeys.DefenseBonus);
                s.Memory.Remove(ReligionKeys.DefenseApplied);
                return;
            }
            s.Memory[ReligionKeys.DefenseBonus] = bonus;
            float cur = Math.Max(0f, MathUtil.Sanitize(s.DefenseStrength));
            if (s.Memory.TryGetValue(ReligionKeys.DefenseApplied, out float applied) && applied == cur) return;
            float bumped = MathUtil.Clamp(cur * (1f + bonus), 0f, 1e9f);
            s.DefenseStrength = bumped;
            s.Memory[ReligionKeys.DefenseApplied] = bumped;
        }

        // ----------------------------------------------------------------------------------------- heresy

        /// <summary>
        /// 0..1 conflict between the god's character and a faith's View of the God: Loving Parent vs a wrathful god,
        /// Wrathful Judge vs a benevolent one, Distant Watcher vs a meddling (or extreme) god, Trickster vs an
        /// utterly consistent one.
        /// </summary>
        public static float ViewConflict(DoctrineId view, float alignment, int castsSinceLastRun)
        {
            float a = MathUtil.Clamp(MathUtil.Sanitize(alignment), -1f, 1f);
            switch (view)
            {
                case DoctrineId.LovingParent: return MathUtil.Clamp01((-a - 0.2f) / 0.6f);
                case DoctrineId.WrathfulJudge: return MathUtil.Clamp01((a - 0.2f) / 0.6f);
                case DoctrineId.DistantWatcher:
                    return Math.Max(MathUtil.Clamp01((castsSinceLastRun - ReligionTuning.DistantWatcherCastTolerance) / 6f),
                                    MathUtil.Clamp01((Math.Abs(a) - 0.8f) / 0.2f));
                case DoctrineId.Trickster: return MathUtil.Clamp01((Math.Abs(a) - 0.75f) / 0.25f);
                default: return 0f;
            }
        }

        private void UpdateHeresy(SimContext ctx, Civilization civ, int delta, float pressure)
        {
            var god = ctx.State.God;
            float heresy = ReligionQueries.Heresy(civ);
            float favor = MathUtil.Clamp(MathUtil.Sanitize(civ.GodFavor), -1f, 1f);
            float devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f));

            float rise = 0f;
            if (favor < ReligionTuning.HeresyFavorThreshold && devotion < ReligionTuning.HeresyDevotionThreshold)
            {
                float severity = MathUtil.Clamp01((ReligionTuning.HeresyFavorThreshold - favor) / (1f + ReligionTuning.HeresyFavorThreshold))
                               + MathUtil.Clamp01((ReligionTuning.HeresyDevotionThreshold - devotion) / ReligionTuning.HeresyDevotionThreshold);
                rise += ReligionTuning.HeresyRisePerTick * delta * (1f + 0.5f * severity);
            }

            var view = _docs[AxisView];
            if (view != null)
            {
                float seen = MathUtil.Sanitize(civ.Mem(ReligionKeys.SeenCasts, god.PowersCast), god.PowersCast);
                int castsSince = Math.Max(0, god.PowersCast - (int)MathUtil.Clamp(seen, 0f, int.MaxValue));
                rise += ReligionTuning.HeresyConflictPerTick * delta * ViewConflict(view.Id, god.Alignment, castsSince);
            }

            float resist = 0f, mult = 1f;
            for (int a = 0; a < _docs.Length; a++)
                if (_docs[a] != null) { resist += _docs[a].ConversionResistance; mult *= _docs[a].HeresyMultiplier; }
            resist = MathUtil.Clamp01(resist);
            rise += ReligionTuning.ConversionHeresyPerTick * delta * MathUtil.Clamp01(pressure) * (1f - resist);
            rise *= MathUtil.Clamp(MathUtil.Sanitize(mult, 1f), 0.5f, 1.5f);

            if (rise > 0f) heresy += rise;
            else heresy -= ReligionTuning.HeresyDecayPerTick * delta * (0.5f + devotion) * (favor > 0f ? 1.5f : 1f);
            civ.Memory[ReligionKeys.Heresy] = MathUtil.Clamp01(MathUtil.Sanitize(heresy));
        }

        // ----------------------------------------------------------------------------------------- schism & reformation

        private void TrySchism(SimContext ctx, Civilization civ)
        {
            var state = ctx.State;
            if (ReligionQueries.Heresy(civ) <= ReligionTuning.SchismThreshold) return;
            if (civ.Memory.ContainsKey(ReligionKeys.ReformationDueTick)) return; // already splintered; reformers are at work

            _scratchIds.Clear();
            var ids = civ.SettlementIds;
            for (int j = 0; j < ids.Count; j++)
            {
                var s = OwnSettlement(state, civ, ids[j]);
                if (s != null && !s.IsCapital && s.Id != civ.CapitalId) _scratchIds.Add(s.Id);
            }
            Settlement target = _scratchIds.Count > 0 ? state.Settlements.Get(_scratchIds[ctx.Rng.Range(0, _scratchIds.Count)]) : null;
            if (target != null)
            {
                target.Unrest = MathUtil.Clamp01(MathUtil.Sanitize(target.Unrest) + ReligionTuning.SchismUnrest);
                target.Memory[ReligionKeys.LastSchismTick] = state.Tick;
            }

            var mem = civ.Memory;
            mem[ReligionKeys.Heresy] = ReligionTuning.HeresyAfterSchism;
            mem[ReligionKeys.SchismCount] = MathUtil.Clamp(MathUtil.Sanitize(civ.Mem(ReligionKeys.SchismCount)), 0f, 1e6f) + 1f;
            mem[ReligionKeys.LastSchismTick] = state.Tick;
            mem[ReligionKeys.ReformationDueTick] = state.Tick + ReligionTuning.ReformationDelayTicks;

            string faith = FaithNameOf(ctx, civ);
            string text = target != null
                ? "Schism! In " + target.Name + ", heretics broke with " + faith + ", smashing its icons and denouncing " + Tenet(AxisView) + ". The streets seethe with unrest."
                : "Schism! Heretics broke with " + faith + " and fled into the wilds, denouncing " + Tenet(AxisView) + ".";
            var at = target ?? PreferredSettlement(state, civ);
            Chronicle(ctx, text, civ.Id, target != null ? target.Id : -1, at != null ? at.Center : Vec2i.Invalid, 2);
        }

        private void TryReformation(SimContext ctx, Civilization civ)
        {
            var state = ctx.State;
            if (!civ.Memory.TryGetValue(ReligionKeys.ReformationDueTick, out float due)) return;
            if (float.IsNaN(due) || float.IsInfinity(due)) { civ.Memory.Remove(ReligionKeys.ReformationDueTick); return; }
            if (state.Tick < (long)due) return;
            civ.Memory.Remove(ReligionKeys.ReformationDueTick);

            // Reform the View when it contradicts the god's deeds; otherwise reformers pick an axis.
            var view = _docs[AxisView];
            int axis = view != null && ViewConflict(view.Id, state.God.Alignment, 0) > 0f ? AxisView : ctx.Rng.Range(0, DoctrineCatalog.AxisCount);
            var old = _docs[axis];
            DoctrineSelector.Features(ReligionQueries.RaceOf(ctx.Db, civ), state.God, civ.Mem(ReligionKeys.HolySiteSacred) > 0.5f, _features);
            var newId = DoctrineSelector.Select((DoctrineAxis)axis, _features, state.Seed, civ.Id, old != null ? (int)old.Id : -1);
            civ.Memory[DoctrineCatalog.MemoryKey((DoctrineAxis)axis)] = (float)newId;
            _docs[axis] = DoctrineCatalog.Get(newId);
            civ.Memory[ReligionKeys.Heresy] = ReligionQueries.Heresy(civ) * ReligionTuning.HeresyAfterReformation;
            civ.Memory[ReligionKeys.ReformationCount] = MathUtil.Clamp(MathUtil.Sanitize(civ.Mem(ReligionKeys.ReformationCount)), 0f, 1e6f) + 1f;

            var nd = _docs[axis];
            string text = "A Reformation swept " + FaithNameOf(ctx, civ) + ": reformers renounced " + (old != null ? old.Name : "the old ways")
                + " and embraced " + (nd != null ? nd.Name : "a new creed") + ", preaching " + (nd != null ? nd.Tenet : "renewal") + ".";
            var at = PreferredSettlement(state, civ);
            Chronicle(ctx, text, civ.Id, at != null ? at.Id : -1, at != null ? at.Center : Vec2i.Invalid, 2);
        }

        // ----------------------------------------------------------------------------------------- conversion

        /// <summary>
        /// 0..1 pressure from contacted faiths whose average settlement Faith exceeds ours by more than
        /// <see cref="ReligionTuning.ConversionFaithGap"/> (scaled by their missionary zeal). Also returns the index
        /// (in Civilizations.Items) of the strongest influencer, -1 if none.
        /// </summary>
        private float ConversionPressure(WorldState state, Civilization civ, int civIndex, out int influencerIndex)
        {
            influencerIndex = -1;
            float mine = civIndex >= 0 && civIndex < _avgFaith.Count ? _avgFaith[civIndex] : -1f;
            if (mine < 0f) return 0f;
            var civs = state.Civilizations.Items;
            float total = 0f, best = 0f;
            for (int j = 0; j < civs.Count && j < _avgFaith.Count; j++)
            {
                if (j == civIndex) continue;
                var other = civs[j];
                if (other == null || !other.IsAlive || other.Id == civ.Id || !ReligionQueries.HasFaith(other)) continue;
                if (!state.Diplomacy.TryGet(civ.Id, other.Id, out var rel) || rel == null || !rel.HasContact) continue;
                float theirs = _avgFaith[j];
                if (theirs < 0f) continue;
                float gap = theirs - mine - ReligionTuning.ConversionFaithGap;
                if (!(gap > 0f)) continue;
                float zeal = 1f;
                for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
                {
                    var d = ReligionQueries.Doctrine(other, (DoctrineAxis)a);
                    if (d != null) zeal *= d.MissionaryZeal;
                }
                float p = gap * MathUtil.Clamp(zeal, 0.5f, 2f);
                total += p;
                if (p > best) { best = p; influencerIndex = j; }
            }
            return MathUtil.Clamp01(MathUtil.Sanitize(total));
        }

        private void TryConversion(SimContext ctx, Civilization civ, int influencerIndex, float pressure, int delta)
        {
            var state = ctx.State;
            if (!(pressure > 0f) || influencerIndex < 0 || influencerIndex >= state.Civilizations.Count) return;
            if (civ.Memory.TryGetValue(ReligionKeys.LastConversionTick, out float last) && !float.IsNaN(last)
                && state.Tick - (long)last < ReligionTuning.ConversionCooldownTicks) return;
            var other = state.Civilizations.Items[influencerIndex];
            if (other == null || !other.IsAlive || !ReligionQueries.HasFaith(other)) return;

            float resist = 0f;
            for (int a = 0; a < _docs.Length; a++) if (_docs[a] != null) resist += _docs[a].ConversionResistance;
            float perYear = ReligionTuning.ConversionSwapPerYear * pressure * (1f - MathUtil.Clamp01(resist));
            if (!ctx.Rng.Chance(Rng.PerYearToPerTicks(perYear, delta))) return;

            _axes.Clear();
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
            {
                var theirs = ReligionQueries.Doctrine(other, (DoctrineAxis)a);
                if (theirs != null && (_docs[a] == null || _docs[a].Id != theirs.Id)) _axes.Add(a);
            }
            if (_axes.Count == 0) return;
            int axis = _axes[ctx.Rng.Range(0, _axes.Count)];
            var old = _docs[axis];
            var adopted = ReligionQueries.Doctrine(other, (DoctrineAxis)axis);
            civ.Memory[DoctrineCatalog.MemoryKey((DoctrineAxis)axis)] = (float)adopted.Id;
            _docs[axis] = adopted;
            civ.Memory[ReligionKeys.LastConversionTick] = state.Tick;
            civ.Memory[ReligionKeys.ConvertedBy] = other.Id;

            string text = "Missionaries of " + FaithNameOf(ctx, other) + " won converts among the " + civ.Name + ": " + FaithNameOf(ctx, civ)
                + " now embraces " + adopted.Name + (old != null ? " in place of " + old.Name : "") + ".";
            var at = PreferredSettlement(state, civ);
            Chronicle(ctx, text, civ.Id, at != null ? at.Id : -1, at != null ? at.Center : Vec2i.Invalid, 1);
        }

        // ----------------------------------------------------------------------------------------- holy war

        /// <summary>
        /// Martial Zeal faiths name the hostile (or at-war) contacted neighbour with an opposing View of the God and the
        /// lowest opinion as their holy war target (<c>rel.holyWarTarget</c>); the key is removed when none qualifies.
        /// </summary>
        private void UpdateHolyWar(SimContext ctx, Civilization civ)
        {
            var state = ctx.State;
            int prev = ReligionQueries.HolyWarTarget(civ);
            var ethos = _docs[AxisEthos];
            var view = _docs[AxisView];
            Civilization target = null;
            if (ethos != null && ethos.Id == DoctrineId.MartialZeal && view != null)
            {
                float worst = float.MaxValue;
                var civs = state.Civilizations.Items;
                for (int j = 0; j < civs.Count; j++)
                {
                    var other = civs[j];
                    if (other == null || !other.IsAlive || other.Id == civ.Id || !ReligionQueries.HasFaith(other)) continue;
                    if (!state.Diplomacy.TryGet(civ.Id, other.Id, out var rel) || rel == null || !rel.HasContact) continue;
                    if (!(rel.AtWar || rel.Stance <= DiplomaticStance.Hostile)) continue;
                    var otherView = ReligionQueries.Doctrine(other, DoctrineAxis.View);
                    if (otherView == null || !DoctrineCatalog.ViewsOppose(view.Id, otherView.Id)) continue;
                    float opinion = MathUtil.Sanitize(rel.Opinion);
                    if (target == null || opinion < worst) { target = other; worst = opinion; }
                }
            }
            if (target == null) { civ.Memory.Remove(ReligionKeys.HolyWarTarget); return; }
            civ.Memory[ReligionKeys.HolyWarTarget] = target.Id;
            if (target.Id == prev) return;

            if (civ.Memory.TryGetValue(ReligionKeys.HolyWarAnnouncedTick, out float last) && !float.IsNaN(last)
                && state.Tick - (long)last < ReligionTuning.HolyWarAnnounceCooldownTicks) return;
            civ.Memory[ReligionKeys.HolyWarAnnouncedTick] = state.Tick;
            var otherViewDef = ReligionQueries.Doctrine(target, DoctrineAxis.View);
            string text = "Zealots of " + FaithNameOf(ctx, civ) + " called the " + civ.Name + " to holy war against the " + target.Name
                + ", whose priests preach " + (otherViewDef != null ? otherViewDef.Tenet : "blasphemy") + ".";
            var at = PreferredSettlement(state, civ);
            Chronicle(ctx, text, civ.Id, at != null ? at.Id : -1, at != null ? at.Center : Vec2i.Invalid, 1);
        }

        // =====================================================================================================
        // helpers
        // =====================================================================================================

        /// <summary>Population-weighted average settlement Faith of a living civ; -1 when it has no living settlement.</summary>
        public static float AverageFaith(WorldState state, Civilization civ)
        {
            if (state == null || civ == null || !civ.IsAlive) return -1f;
            double sum = 0, weight = 0;
            var ids = civ.SettlementIds;
            for (int j = 0; j < ids.Count; j++)
            {
                var s = OwnSettlement(state, civ, ids[j]);
                if (s == null) continue;
                double w = Math.Max(1, s.Population);
                sum += MathUtil.Clamp01(MathUtil.Sanitize(s.Faith, 0.5f)) * w;
                weight += w;
            }
            return weight > 0 ? (float)(sum / weight) : -1f;
        }

        private static Settlement OwnSettlement(WorldState state, Civilization civ, int id)
        {
            if (id < 0) return null;
            var s = state.Settlements.Get(id);
            return s != null && s.IsAlive && s.CivilizationId == civ.Id ? s : null;
        }

        /// <summary>The capital if alive, else the first living settlement, else null.</summary>
        private static Settlement PreferredSettlement(WorldState state, Civilization civ)
        {
            var cap = OwnSettlement(state, civ, civ.CapitalId);
            if (cap != null) return cap;
            var ids = civ.SettlementIds;
            for (int j = 0; j < ids.Count; j++)
            {
                var s = OwnSettlement(state, civ, ids[j]);
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>First settlement (capital first) hosting a completed religious building; null if none.</summary>
        private static Settlement FaithBuildingSettlement(WorldState state, Civilization civ)
        {
            var cap = OwnSettlement(state, civ, civ.CapitalId);
            if (cap != null && HasFaithBuilding(state, cap)) return cap;
            var ids = civ.SettlementIds;
            for (int j = 0; j < ids.Count; j++)
            {
                var s = OwnSettlement(state, civ, ids[j]);
                if (s != null && HasFaithBuilding(state, s)) return s;
            }
            return null;
        }

        private static bool HasFaithBuilding(WorldState state, Settlement s)
        {
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b != null && !b.IsDestroyed && b.IsComplete && FaithBuildingRank(b.Type) > 0) return true;
            }
            return false;
        }

        private static string FaithNameOf(SimContext ctx, Civilization civ) =>
            ReligionNames.FaithName(civ.Id, ctx.State.Seed, ReligionQueries.RaceOf(ctx.Db, civ));

        private static void Chronicle(SimContext ctx, string text, int civId, int settlementId, Vec2i position, int importance)
        {
            if (ctx.Ops != null) { ctx.Ops.AddChronicle(ChronicleCategory.Divine, text, civId, settlementId, position, importance); return; }
            ctx.State.Chronicle.Add(new ChronicleEntry
            {
                Tick = ctx.State.Tick, Category = ChronicleCategory.Divine, Text = text ?? "", CivilizationId = civId,
                SettlementId = settlementId, Position = position, Importance = MathUtil.Clamp(importance, 0, 3)
            });
        }

        private static void SetOrRemove(Dictionary<string, float> mem, string key, float value)
        {
            value = MathUtil.Sanitize(value);
            if (value == 0f) mem.Remove(key); else mem[key] = value;
        }

        /// <summary>Repairs NaN/Infinity/out-of-range values in this module's civ keys (corrupt or hand-edited saves).</summary>
        private static void SanitizeMemory(Civilization civ)
        {
            var mem = civ.Memory;
            if (mem.TryGetValue(ReligionKeys.Founded, out float f) && (float.IsNaN(f) || float.IsInfinity(f))) mem.Remove(ReligionKeys.Founded);
            Fix(mem, ReligionKeys.Heresy, 0f, 1f, 0f);
            Fix(mem, ReligionKeys.HolySite, -1f, int.MaxValue, -1f);
            Fix(mem, ReligionKeys.HolySiteSacred, 0f, 1f, 0f);
            Fix(mem, ReligionKeys.ConversionPressure, 0f, 1f, 0f);
            Fix(mem, ReligionKeys.SeenCasts, 0f, 1e9f, 0f);
            Fix(mem, ReligionKeys.ResearchBonus, 0f, ReligionTuning.MaxResearchPerTick, 0f);
            Fix(mem, ReligionKeys.DefenseBonus, 0f, ReligionTuning.MaxDefenseBonus, 0f);
            Fix(mem, ReligionKeys.SchismCount, 0f, 1e6f, 0f);
            Fix(mem, ReligionKeys.ReformationCount, 0f, 1e6f, 0f);
            if (mem.TryGetValue(ReligionKeys.ReformationDueTick, out float due) && (float.IsNaN(due) || float.IsInfinity(due))) mem.Remove(ReligionKeys.ReformationDueTick);
            if (mem.TryGetValue(ReligionKeys.HolyWarTarget, out float hw) && (float.IsNaN(hw) || float.IsInfinity(hw))) mem.Remove(ReligionKeys.HolyWarTarget);
            if (mem.TryGetValue(ReligionKeys.LastConversionTick, out float lc) && (float.IsNaN(lc) || float.IsInfinity(lc))) mem.Remove(ReligionKeys.LastConversionTick);
            if (mem.TryGetValue(ReligionKeys.HolyWarAnnouncedTick, out float ha) && (float.IsNaN(ha) || float.IsInfinity(ha))) mem.Remove(ReligionKeys.HolyWarAnnouncedTick);
        }

        private static void Fix(Dictionary<string, float> mem, string key, float min, float max, float fallback)
        {
            if (!mem.TryGetValue(key, out float v)) return;
            float clean = MathUtil.Clamp(MathUtil.Sanitize(v, fallback), min, max);
            if (clean != v) mem[key] = clean;
        }
    }
}
