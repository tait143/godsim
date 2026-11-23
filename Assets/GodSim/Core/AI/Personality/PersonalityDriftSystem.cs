using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Makes civilization personalities a product of their history. Event handlers accumulate decaying per-civ
    /// experience counters ("pers.exp.*" in <c>Civilization.Memory</c>); every run each "ai.trait.*" value moves at most
    /// <see cref="PersonalityModel.MaxStepPerRun"/> toward race default + history deltas (bounded to default ± 0.35).
    /// Notable shifts (±0.2 from the default) are chronicled, at most once per trait per 50 years.
    /// Stateless apart from reusable scratch buffers: everything persistent lives in civ memory. Deterministic, no RNG.
    /// </summary>
    public sealed class PersonalityDriftSystem : ISimSystem
    {
        public string Name => "AI.Personality";
        public int TickInterval => 48;

        private SimContext _ctx;
        private readonly float[] _contrib = new float[PersonalityKeys.TraitCount * PersonalityKeys.CauseCount];
        private readonly List<int> _civScratch = new List<int>(8);
        private long _lastWrathTick = -1;
        private Vec2i _lastWrathPos = Vec2i.Invalid;
        private int _lastWrathRadius;

        public void Initialize(SimContext ctx)
        {
            _ctx = ctx;
            if (ctx == null || ctx.Events == null) return;
            var ev = ctx.Events;
            ev.Subscribe<WarDeclaredEvent>(OnWarDeclared);
            ev.Subscribe<PeaceSignedEvent>(OnPeaceSigned);
            ev.Subscribe<BattleEvent>(OnBattle);
            ev.Subscribe<SettlementCapturedEvent>(OnCaptured);
            ev.Subscribe<SettlementDestroyedEvent>(OnDestroyed);
            ev.Subscribe<GodPowerCastEvent>(OnGodPower);
            ev.Subscribe<DisasterEvent>(OnDisaster);
            ev.Subscribe<FamineEvent>(OnFamine);
            ev.Subscribe<AgeAdvancedEvent>(OnAgeAdvanced);
            ev.Subscribe<TechResearchedEvent>(OnTech);
            ev.Subscribe<SettlementFoundedEvent>(OnFounded);
            ev.Subscribe<CivilizationFoundedEvent>(OnCivFounded);
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) if (civs[i].IsAlive) EnsureInitialized(civs[i], ctx.Db);
        }

        public void Tick(SimContext ctx)
        {
            _ctx = ctx;
            var db = ctx.Db;
            var civs = ctx.State.Civilizations.Items;
            float years = Math.Max(1, ctx.DeltaTicks) / (float)SimTime.TicksPerYear;
            float decay = (float)Math.Pow(0.5, years / PersonalityModel.ExperienceHalfLifeYears);
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                EnsureInitialized(civ, db);
                Decay(civ, decay);
                AccrueStanding(ctx, civ, years);
                civ.Memory[PersonalityKeys.FamineThisRun] = 0f;
                PersonalityModel.Contributions(civ, db, _contrib);
                for (int t = 0; t < PersonalityKeys.TraitCount; t++) DriftTrait(ctx, civ, (AITrait)t);
            }
        }

        // ------------------------------------------------------------------ per-run work
        /// <summary>Seeds every trait from the race defaults the first time a civ is seen.</summary>
        public static void EnsureInitialized(Civilization civ, GameDatabase db)
        {
            if (civ == null || civ.Memory.ContainsKey(PersonalityKeys.Initialized)) return;
            for (int t = 0; t < PersonalityKeys.TraitCount; t++)
                AdvisorBoard.SetTrait(civ, (AITrait)t, AdvisorBoard.RaceTrait(db, civ.Race, (AITrait)t));
            civ.Memory[PersonalityKeys.Initialized] = 1f;
        }

        private static void Decay(Civilization civ, float factor)
        {
            var mem = civ.Memory;
            for (int c = 0; c < PersonalityKeys.CauseCount; c++)
            {
                string key = PersonalityKeys.ExperienceKey((PersonalityCause)c);
                if (!mem.TryGetValue(key, out float v)) continue;
                v = MathUtil.Sanitize(v) * factor;
                mem[key] = v < 0.001f ? 0f : MathUtil.Clamp(v, 0f, PersonalityModel.MaxExperience);
            }
        }

        /// <summary>Continuous experiences: years at peace, trade agreements, contacted races, crowding.</summary>
        private static void AccrueStanding(SimContext ctx, Civilization civ, float years)
        {
            var civs = ctx.State.Civilizations.Items;
            var dip = ctx.State.Diplomacy;
            bool atWar = false;
            int trades = 0, raceMask = 0;
            for (int j = 0; j < civs.Count; j++)
            {
                var o = civs[j];
                if (o == null || o.Id == civ.Id || !o.IsAlive) continue;
                if (!dip.TryGet(civ.Id, o.Id, out var r) || r == null) continue;
                if (r.AtWar) { atWar = true; continue; }
                if (r.TradeAgreement) trades++;
                if (r.HasContact && o.Race != civ.Race) raceMask |= 1 << ((int)o.Race & 31);
            }
            int races = 0;
            for (int m = raceMask; m != 0; m &= m - 1) races++;
            if (!atWar) AddExp(civ, PersonalityCause.Peace, years);
            if (trades > 0) AddExp(civ, PersonalityCause.Trade, trades * years);
            if (races > 0) AddExp(civ, PersonalityCause.Contact, races * years);

            int total = 0, blocked = 0;
            var ids = civ.SettlementIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var s = ctx.State.Settlements.Get(ids[k]);
                if (s == null || !s.IsAlive) continue;
                total++;
                if (s.Memory.TryGetValue(PersonalityKeys.ExpandBlockedUntil, out float until) && MathUtil.Sanitize(until) > ctx.Tick) blocked++;
            }
            if (total > 0 && blocked > 0) AddExp(civ, PersonalityCause.Crowding, years * blocked / total);
        }

        private void DriftTrait(SimContext ctx, Civilization civ, AITrait trait)
        {
            var db = ctx.Db;
            float race = AdvisorBoard.RaceTrait(db, civ.Race, trait);
            float current = PersonalityModel.ClampToBounds(race, AdvisorBoard.Trait(civ, db, trait));
            float target = PersonalityModel.Target(db, civ.Race, trait, _contrib);
            float next = PersonalityModel.ClampToBounds(race, PersonalityModel.Step(current, target, PersonalityModel.MaxStepPerRun));
            AdvisorBoard.SetTrait(civ, trait, next);

            float dev = next - race;
            string bandKey = PersonalityKeys.BandKey(trait);
            int band = (int)MathUtil.Sanitize(civ.Mem(bandKey));
            int newBand = band;
            if (dev >= PersonalityModel.NotableDeviation - 1e-5f) newBand = 1;
            else if (dev <= -PersonalityModel.NotableDeviation + 1e-5f) newBand = -1;
            else if (Math.Abs(dev) < PersonalityModel.CalmDeviation) newBand = 0;
            if (newBand == band) return;
            civ.Memory[bandKey] = newBand;
            if (newBand == 0) return;

            int year = SimTime.Year(ctx.Tick);
            string chronKey = PersonalityKeys.ChronicleYearKey(trait);
            if (civ.Memory.TryGetValue(chronKey, out float last) && year - (int)MathUtil.Sanitize(last) < PersonalityModel.ChronicleCooldownYears) return;
            civ.Memory[chronKey] = year;
            int cause = PersonalityModel.DominantCause(_contrib, trait, newBand);
            var capital = ctx.State.Settlements.Get(civ.CapitalId);
            ctx.Ops.AddChronicle(ChronicleCategory.Civilization, PersonalityText.Shift(civ, db, trait, newBand > 0, cause),
                civ.Id, capital != null ? capital.Id : -1, capital != null ? capital.Center : Vec2i.Invalid, 1);
        }

        // ------------------------------------------------------------------ experience helpers
        private static void AddExp(Civilization civ, PersonalityCause cause, float amount)
        {
            if (civ == null || !civ.IsAlive) return;
            amount = MathUtil.Sanitize(amount);
            if (amount == 0f) return;
            string key = PersonalityKeys.ExperienceKey(cause);
            civ.Memory[key] = MathUtil.Clamp(MathUtil.Sanitize(civ.Mem(key)) + amount, 0f, PersonalityModel.MaxExperience);
        }

        private static void ScaleExp(Civilization civ, PersonalityCause cause, float factor)
        {
            if (civ == null) return;
            string key = PersonalityKeys.ExperienceKey(cause);
            if (civ.Memory.TryGetValue(key, out float v)) civ.Memory[key] = MathUtil.Clamp(MathUtil.Sanitize(v) * factor, 0f, PersonalityModel.MaxExperience);
        }

        private Civilization Civ(int id)
        {
            if (id < 0 || _ctx == null) return null;
            var c = _ctx.State.Civilizations.Get(id);
            if (c == null || !c.IsAlive) return null;
            EnsureInitialized(c, _ctx.Db);
            return c;
        }

        private bool IsAtWar(Civilization civ)
        {
            var civs = _ctx.State.Civilizations.Items;
            for (int j = 0; j < civs.Count; j++)
                if (civs[j] != null && civs[j].Id != civ.Id && _ctx.State.Diplomacy.AreAtWar(civ.Id, civs[j].Id)) return true;
            return false;
        }

        /// <summary>Fills _civScratch with ids of civs owning an alive settlement whose territory touches the area.</summary>
        private void CollectCivsNear(Vec2i pos, int radius)
        {
            _civScratch.Clear();
            if (!pos.IsValid) return;
            radius = MathUtil.Clamp(radius, 0, 256);
            var items = _ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (s == null || !s.IsAlive || s.CivilizationId < 0) continue;
                long r = radius + MathUtil.Clamp(s.TerritoryRadius, 0, 64);
                long dx = s.Center.X - (long)pos.X, dy = s.Center.Y - (long)pos.Y;
                if (dx * dx + dy * dy > r * r) continue;
                if (!_civScratch.Contains(s.CivilizationId)) _civScratch.Add(s.CivilizationId);
            }
        }

        /// <summary>+1 blessing, -1 wrath, 0 shaping/creation.</summary>
        private static int Classify(GameDatabase db, GodPowerType p)
        {
            int i = (int)p;
            if (db != null && i < db.GodPowers.Count && db.GodPowers[i] != null && db.GodPowers[i].Category == GodPowerCategory.Wrath) return -1;
            if (p <= GodPowerType.Prophet) return 1;
            if (p >= GodPowerType.Lightning) return -1;
            return 0;
        }

        // ------------------------------------------------------------------ event handlers
        private void OnCivFounded(CivilizationFoundedEvent e) { Civ(e.CivilizationId); }

        private void OnWarDeclared(WarDeclaredEvent e)
        {
            var a = Civ(e.AttackerId); var d = Civ(e.DefenderId);
            ScaleExp(a, PersonalityCause.Peace, 0.25f);
            ScaleExp(d, PersonalityCause.Peace, 0.25f);
            AddExp(d, PersonalityCause.Invaded, 1f);
        }

        private void OnPeaceSigned(PeaceSignedEvent e)
        {
            var a = Civ(e.AttackerId); var d = Civ(e.DefenderId);
            float s = MathUtil.Clamp(MathUtil.Sanitize(e.WarScore), -100f, 100f);
            if (Math.Abs(s) <= 15f) return;
            float w = 1f + 2f * Math.Abs(s) / 100f;
            AddExp(s > 0 ? a : d, PersonalityCause.Victory, w);
            AddExp(s > 0 ? d : a, PersonalityCause.Defeat, w);
        }

        private void OnBattle(BattleEvent e)
        {
            var a = Civ(e.AttackerCivId); var d = Civ(e.DefenderCivId);
            AddExp(e.AttackerWon ? a : d, PersonalityCause.Victory, 1f);
            AddExp(e.AttackerWon ? d : a, PersonalityCause.Defeat, 1f);
            ScaleExp(a, PersonalityCause.Peace, 0.9f);
            ScaleExp(d, PersonalityCause.Peace, 0.9f);
        }

        private void OnCaptured(SettlementCapturedEvent e)
        {
            var winner = Civ(e.NewCivilizationId);
            AddExp(winner, PersonalityCause.Victory, 3f);
            AddExp(winner, PersonalityCause.Expansion, 0.5f);
            AddExp(Civ(e.OldCivilizationId), PersonalityCause.Defeat, 3f);
        }

        private void OnDestroyed(SettlementDestroyedEvent e)
        {
            var civ = Civ(e.CivilizationId);
            if (civ == null) return;
            if (IsAtWar(civ)) AddExp(civ, PersonalityCause.Defeat, 2f);
            else AddExp(civ, PersonalityCause.Hardship, 1f);
        }

        private void OnGodPower(GodPowerCastEvent e)
        {
            if (!e.Success || _ctx == null) return;
            int kind = Classify(_ctx.Db, e.Power);
            if (kind == 0) return;
            if (kind < 0) { _lastWrathTick = _ctx.Tick; _lastWrathPos = e.Target; _lastWrathRadius = e.Radius; }
            CollectCivsNear(e.Target, e.Radius);
            for (int i = 0; i < _civScratch.Count; i++)
                AddExp(Civ(_civScratch[i]), kind > 0 ? PersonalityCause.Blessing : PersonalityCause.Wrath, 1f);
        }

        private void OnDisaster(DisasterEvent e)
        {
            if (_ctx == null) return;
            // A disaster spawned by a wrath power this tick is already counted as wrath.
            if (_lastWrathTick == _ctx.Tick && _lastWrathPos.IsValid && e.Position.IsValid
                && e.Position.ChebyshevTo(_lastWrathPos) <= Math.Max(1, _lastWrathRadius)) return;
            float amount = 0.5f + MathUtil.Clamp(MathUtil.Sanitize(e.Severity), 0f, 1.5f);
            CollectCivsNear(e.Position, e.Radius);
            for (int i = 0; i < _civScratch.Count; i++) AddExp(Civ(_civScratch[i]), PersonalityCause.Hardship, amount);
        }

        private void OnFamine(FamineEvent e)
        {
            if (_ctx == null) return;
            var s = _ctx.State.Settlements.Get(e.SettlementId);
            if (s == null) return;
            var civ = Civ(s.CivilizationId);
            if (civ == null) return;
            const float CapPerRun = 2.5f;
            float run = MathUtil.Sanitize(civ.Mem(PersonalityKeys.FamineThisRun));
            float amount = Math.Min(0.4f + 0.6f * MathUtil.Clamp01(MathUtil.Sanitize(e.Shortfall)), CapPerRun - run);
            if (amount <= 0f) return;
            civ.Memory[PersonalityKeys.FamineThisRun] = run + amount;
            AddExp(civ, PersonalityCause.Hardship, amount);
        }

        private void OnAgeAdvanced(AgeAdvancedEvent e) { AddExp(Civ(e.CivilizationId), PersonalityCause.Learning, 4f); }

        private void OnTech(TechResearchedEvent e) { AddExp(Civ(e.CivilizationId), PersonalityCause.Learning, 1f); }

        private void OnFounded(SettlementFoundedEvent e)
        {
            var civ = Civ(e.CivilizationId);
            if (civ == null) return;
            if (e.ParentSettlementId < 0 && civ.SettlementIds.Count <= 1) return; // the homeland is not an expansion
            AddExp(civ, PersonalityCause.Expansion, 1f);
            ScaleExp(civ, PersonalityCause.Crowding, 0.7f);
        }
    }
}
