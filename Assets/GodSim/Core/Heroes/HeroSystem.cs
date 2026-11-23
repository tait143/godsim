using System;
using System.Collections.Generic;

namespace GodSim.Core.Heroes
{
    /// <summary>
    /// Notable individuals who give history faces. Every run (default 24 ticks) it:
    /// <list type="number">
    /// <item>reads the hero registry from <see cref="WorldState.Memory"/> (sorted, deterministic);</item>
    /// <item>writes a biography for heroes found dead or missing (fallback for deaths it did not hear about);</item>
    /// <item>applies each living hero's small, bounded role effect;</item>
    /// <item>per alive civilization (max 3 living heroes, 1 per settlement) may raise a promising adult.</item>
    /// </list>
    /// Deaths are normally handled immediately through <see cref="PersonDiedEvent"/>. All persistent data is in
    /// <c>WorldState.Memory</c>; fields here are caches/scratch only, so a fresh instance resumes a loaded world.
    /// </summary>
    public sealed class HeroSystem : ISimSystem
    {
        /// <summary>Default run interval in ticks.</summary>
        public const int Interval = HeroRules.ReferenceInterval;

        private readonly Action<PersonDiedEvent> _onDied;
        private readonly Action<BuildingCompletedEvent> _onBuilt;
        private SimContext _ctx;
        private EventBus _bus;

        // scratch (reused; never persisted)
        private readonly List<int> _heroIds = new List<int>();
        private readonly List<Person> _living = new List<Person>();
        private readonly List<HeroRole> _livingRoles = new List<HeroRole>();
        private readonly List<int> _architects = new List<int>();
        private readonly List<int> _scratchIds = new List<int>();

        public HeroSystem()
        {
            _onDied = OnPersonDied;
            _onBuilt = OnBuildingCompleted;
        }

        public string Name => "Heroes";
        public int TickInterval => Interval;

        /// <summary>Multiplier on the per-civ raise chance (balance/testing knob; 0 disables new heroes).</summary>
        public float RaiseChanceScale { get; set; } = 1f;
        /// <summary>Multiplier on the explorer discovery chance (balance/testing knob).</summary>
        public float DiscoveryChanceScale { get; set; } = 1f;

        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            Attach(ctx);
            RebuildArchitectCache(ctx.State);
        }

        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            if (_ctx != ctx) _ctx = ctx; // Tick without Initialize: work, but rely on the death scan.
            var state = ctx.State;
            float scale = MathUtil.Clamp(ctx.DeltaTicks, 1, 10000) / (float)HeroRules.ReferenceInterval;

            // 1-2. registry + death detection (fallback)
            HeroKeys.CollectHeroIds(state.Memory, _heroIds);
            _living.Clear();
            _livingRoles.Clear();
            _architects.Clear();
            for (int i = 0; i < _heroIds.Count; i++)
            {
                int id = _heroIds[i];
                if (!HeroKeys.TryGetRole(state.Memory, id, out var role))
                {
                    HeroKeys.Clear(state.Memory, id); // corrupt entry
                    continue;
                }
                var p = state.People.Get(id);
                if (p == null || !p.IsAlive)
                {
                    Bury(ctx, id, role, p, p != null ? p.AgeYears : -1f, p != null ? (DeathCause?)p.DeathCause : null);
                    continue;
                }
                _living.Add(p);
                _livingRoles.Add(role);
                if (role == HeroRole.Architect) _architects.Add(id);
            }

            // 3. effects
            int livingBeforeRaise = _living.Count;
            for (int i = 0; i < livingBeforeRaise; i++)
                ApplyEffect(ctx, _living[i], _livingRoles[i], scale);

            // 4. raise new heroes
            if (RaiseChanceScale > 0f)
            {
                var civs = state.Civilizations.Items;
                for (int c = 0; c < civs.Count; c++)
                {
                    var civ = civs[c];
                    if (civ == null || !civ.IsAlive) continue;
                    TryRaiseIn(ctx, civ);
                }
            }
        }

        // =============================================================== raising

        /// <summary>
        /// Makes a person a hero of the given role immediately, bypassing chance, caps and job/skill checks
        /// (the person must still be a living adult who is not already a hero). For Composition hooks (e.g. a
        /// divine "Prophet" power), scripted scenarios and tests. Returns false if not possible.
        /// </summary>
        public bool Promote(SimContext ctx, Person p, HeroRole role)
        {
            if (ctx == null || ctx.State == null || p == null || !p.IsAlive || !p.IsAdult) return false;
            if (_ctx == null) _ctx = ctx;
            var state = ctx.State;
            if (state.Memory.ContainsKey(HeroKeys.Role(p.Id))) return false;
            var s = state.Settlements.Get(p.SettlementId);
            var civ = state.Civilizations.Get(p.CivilizationId);
            Raise(ctx, p, role, s, civ);
            return true;
        }

        private void TryRaiseIn(SimContext ctx, Civilization civ)
        {
            var state = ctx.State;
            int heroes = 0;
            for (int i = 0; i < _living.Count; i++) if (_living[i].IsAlive && _living[i].CivilizationId == civ.Id) heroes++;
            if (heroes >= HeroRules.MaxHeroesPerCiv) return;

            int pop = 0;
            var sids = civ.SettlementIds;
            for (int i = 0; i < sids.Count; i++)
            {
                var s = state.Settlements.Get(sids[i]);
                if (s != null && s.IsAlive) pop += s.Population;
            }
            float perYear = HeroRules.RaiseChancePerYear(pop, civ.Age) * MathUtil.Sanitize(RaiseChanceScale);
            if (!(perYear > 0f)) return;
            if (!ctx.Rng.Chance(Rng.PerYearToPerTicks(perYear, ctx.DeltaTicks))) return;

            Person best = null;
            Settlement bestS = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < sids.Count; i++)
            {
                var s = state.Settlements.Get(sids[i]);
                if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) continue;
                if (SettlementHeroCount(s.Id) >= HeroRules.MaxHeroesPerSettlement) continue;
                var res = s.ResidentIds;
                for (int r = 0; r < res.Count; r++)
                {
                    var p = state.People.Get(res[r]);
                    if (!HeroRules.IsEligible(p) || p.SettlementId != s.Id) continue;
                    if (p.HasTrait(PersonTraits.Hero) && state.Memory.ContainsKey(HeroKeys.Role(p.Id))) continue;
                    float score = HeroRules.PromiseScore(p);
                    if (score > bestScore) { bestScore = score; best = p; bestS = s; }
                }
            }
            if (best == null) return;
            var role = HeroRules.RoleForJob(best.Job);
            if (role == null) return;
            Raise(ctx, best, role.Value, bestS, civ);
        }

        private int SettlementHeroCount(int settlementId)
        {
            int n = 0;
            for (int i = 0; i < _living.Count; i++) if (_living[i].IsAlive && _living[i].SettlementId == settlementId) n++;
            return n;
        }

        private void Raise(SimContext ctx, Person p, HeroRole role, Settlement s, Civilization civ)
        {
            var state = ctx.State;
            p.Traits |= PersonTraits.Hero | HeroRules.RoleTrait(role);
            int id = p.Id;
            state.Memory[HeroKeys.Role(id)] = (float)(int)role;
            state.Memory[HeroKeys.Deeds(id)] = 0f;
            state.Memory[HeroKeys.Since(id)] = (float)state.Tick;
            state.Memory[HeroKeys.Home(id)] = s != null ? s.Id : -1f;
            state.Memory[HeroKeys.Civ(id)] = civ != null ? civ.Id : -1f;

            int importance = 1;
            if (civ != null)
            {
                float raised = civ.Mem(HeroKeys.CivRaisedCount, 0f);
                if (!(raised >= 1f)) importance = 2; // a civilization's first hero is a milestone
                civ.Memory[HeroKeys.CivRaisedCount] = MathUtil.Clamp(MathUtil.Sanitize(raised) + 1f, 0f, 1e6f);
            }
            _living.Add(p);
            _livingRoles.Add(role);
            if (role == HeroRole.Architect && !_architects.Contains(id)) _architects.Add(id);

            ctx.Ops?.AddChronicle(ChronicleCategory.Civilization, HeroChronicle.RiseText(p.Name, role, s?.Name),
                civ != null ? civ.Id : p.CivilizationId, s != null ? s.Id : -1, s != null ? s.Center : p.Position, importance);
        }

        // =============================================================== effects

        private void ApplyEffect(SimContext ctx, Person p, HeroRole role, float scale)
        {
            var state = ctx.State;
            if (p == null || !p.IsAlive) return;
            var s = state.Settlements.Get(p.SettlementId);
            var civ = state.Civilizations.Get(p.CivilizationId);
            bool sOk = s != null && s.IsAlive;
            bool cOk = civ != null && civ.IsAlive;
            switch (role)
            {
                case HeroRole.General: General(ctx, p, sOk ? s : null); break;
                case HeroRole.Sage:
                    if (!cOk) break;
                    float rp = HeroRules.SageResearch(civ.Age) * scale;
                    civ.ResearchPool = MathUtil.Sanitize(civ.ResearchPool + rp, civ.ResearchPool);
                    civ.TotalResearch = MathUtil.Sanitize(civ.TotalResearch + rp, civ.TotalResearch);
                    AddDeeds(state, p.Id, HeroRules.SteadyDeedsPerRun * scale);
                    break;
                case HeroRole.Prophet:
                    if (cOk) civ.Devotion = HeroRules.Nudge(civ.Devotion, HeroRules.ProphetDevotion * scale, 1f);
                    if (sOk) s.Faith = HeroRules.Nudge(s.Faith, HeroRules.ProphetFaith * scale, 1f);
                    if (cOk || sOk) AddDeeds(state, p.Id, HeroRules.SteadyDeedsPerRun * scale);
                    break;
                case HeroRole.Architect: if (sOk) Architect(ctx, s, scale); break;
                case HeroRole.Explorer: if (sOk) Explorer(ctx, p, s); break;
                case HeroRole.MerchantPrince:
                    if (!sOk) break;
                    var age = cOk ? civ.Age : AgeId.Dawn;
                    s.Stockpile.Add(ResourceType.Gold, HeroRules.MerchantGold(age, p.Skill) * scale);
                    AddDeeds(state, p.Id, HeroRules.SteadyDeedsPerRun * scale);
                    break;
                case HeroRole.Healer:
                    if (!sOk) break;
                    s.Health = HeroRules.Nudge(s.Health, HeroRules.HealerSettlementHealth * scale, 1f);
                    NudgeResidents(state, s, HeroRules.HealerResidentHealth * scale, true);
                    AddDeeds(state, p.Id, HeroRules.SteadyDeedsPerRun * scale);
                    break;
                case HeroRole.Bard:
                    if (!sOk) break;
                    s.Happiness = HeroRules.Nudge(s.Happiness, HeroRules.BardSettlementHappiness * scale, 1f);
                    NudgeResidents(state, s, HeroRules.BardResidentHappiness * scale, false);
                    AddDeeds(state, p.Id, HeroRules.SteadyDeedsPerRun * scale);
                    break;
            }
        }

        private static void General(SimContext ctx, Person p, Settlement s)
        {
            var state = ctx.State;
            var armies = state.Diplomacy.Armies;
            float deeds = 0f;
            if (p.ArmyId >= 0)
            {
                var a = armies.Get(p.ArmyId);
                if (a != null && a.IsAlive)
                {
                    a.Morale = HeroRules.Nudge(a.Morale, HeroRules.GeneralMoraleBonus, HeroRules.GeneralMoraleCap);
                    if (a.State == ArmyState.Marching || a.State == ArmyState.Besieging) deeds += HeroRules.GeneralCampaignDeedsPerRun;
                }
            }
            if (s != null && IsBesieged(state, s))
            {
                int d = (int)ServiceType.Defense;
                float v = MathUtil.Sanitize(s.ServiceOutput[d]);
                if (v > 0f) s.ServiceOutput[d] = MathUtil.Sanitize(v * HeroRules.GeneralSiegeDefenseMultiplier, v);
                deeds += HeroRules.GeneralSiegeDeedsPerRun;
            }
            if (deeds > 0f) AddDeeds(state, p.Id, deeds);
        }

        private static bool IsBesieged(WorldState state, Settlement s)
        {
            var items = state.Diplomacy.Armies.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var a = items[i];
                if (a != null && a.IsAlive && a.State == ArmyState.Besieging && a.TargetSettlementId == s.Id && a.CivilizationId != s.CivilizationId)
                    return true;
            }
            return false;
        }

        private void Architect(SimContext ctx, Settlement s, float scale)
        {
            var state = ctx.State;
            _scratchIds.Clear();
            _scratchIds.AddRange(s.BuildingIds); // CompleteBuilding publishes events; iterate a copy
            float step = HeroRules.ArchitectProgress * scale;
            for (int i = 0; i < _scratchIds.Count; i++)
            {
                var b = state.Buildings.Get(_scratchIds[i]);
                if (b == null || b.IsDestroyed || b.IsComplete || !b.CostPaid || b.SettlementId != s.Id) continue;
                float prog = MathUtil.Clamp01(MathUtil.Sanitize(b.ConstructionProgress) + step);
                b.ConstructionProgress = prog;
                if (prog >= 1f - MathUtil.Epsilon)
                {
                    b.ConstructionProgress = 1f;
                    if (ctx.Ops != null) ctx.Ops.CompleteBuilding(b); // deeds counted by OnBuildingCompleted
                }
            }
        }

        private void Explorer(SimContext ctx, Person p, Settlement s)
        {
            var state = ctx.State;
            var rng = ctx.Rng;
            float perYear = MathUtil.Clamp01(HeroRules.ExplorerDiscoveriesPerYear * MathUtil.Sanitize(DiscoveryChanceScale));
            if (!rng.Chance(Rng.PerYearToPerTicks(perYear, ctx.DeltaTicks))) return;
            var map = state.Map;
            int baseR = MathUtil.Clamp(s.TerritoryRadius, 2, 64);
            const TileFlags blocked = TileFlags.Ruins | TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit
                | TileFlags.CoalDeposit | TileFlags.OilDeposit | TileFlags.Road | TileFlags.Wall | TileFlags.Trees | TileFlags.Burning;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int dist = baseR + rng.Range(-1, 4);
                int t = rng.Range(-dist, dist + 1);
                int side = rng.Range(0, 4);
                int dx, dy;
                switch (side)
                {
                    case 0: dx = t; dy = dist; break;
                    case 1: dx = dist; dy = t; break;
                    case 2: dx = t; dy = -dist; break;
                    default: dx = -dist; dy = t; break;
                }
                int x = s.Center.X + dx, y = s.Center.Y + dy;
                if (!map.InBounds(x, y)) continue;
                int idx = map.Index(x, y);
                var biome = map.Biomes[idx];
                if (biome.IsWater() || map.BuildingAt[idx] >= 0 || (map.Flags[idx] & blocked) != 0) continue;
                float ruinsRoll = rng.NextFloat();
                float depositRoll = rng.NextFloat();
                var found = HeroRules.DiscoveryFor(biome, ruinsRoll, depositRoll);
                map.Set(idx, found);
                if (found != TileFlags.Ruins)
                    map.Resource[idx] = Math.Max(MathUtil.Sanitize(map.Resource[idx]), rng.Range(200f, 400f));
                map.MarkTerrainChanged();
                ctx.Events?.Publish(new TerrainChangedEvent { MinX = x, MinY = y, MaxX = x, MaxY = y });
                AddDeeds(state, p.Id, 1f);
                ctx.Ops?.AddChronicle(ChronicleCategory.World, HeroChronicle.DiscoveryText(p.Name, found, s.Name),
                    s.CivilizationId, s.Id, new Vec2i(x, y), found == TileFlags.Ruins ? 1 : 0);
                return;
            }
        }

        private static void NudgeResidents(WorldState state, Settlement s, float delta, bool health)
        {
            var res = s.ResidentIds;
            for (int i = 0; i < res.Count; i++)
            {
                var r = state.People.Get(res[i]);
                if (r == null || !r.IsAlive) continue;
                if (health) r.Health = HeroRules.Nudge(r.Health, delta, 1f);
                else r.Happiness = HeroRules.Nudge(r.Happiness, delta, 1f);
            }
        }

        private static void AddDeeds(WorldState state, int personId, float amount)
        {
            if (!(amount > 0f)) return;
            string key = HeroKeys.Deeds(personId);
            float cur = HeroKeys.Read(state.Memory, key, 0f);
            state.Memory[key] = MathUtil.Clamp(cur + amount, 0f, HeroRules.MaxDeeds);
        }

        // =============================================================== deaths

        private void Bury(SimContext ctx, int id, HeroRole role, Person p, float ageYears, DeathCause? cause)
        {
            var state = ctx.State;
            float deedsF = HeroKeys.Read(state.Memory, HeroKeys.Deeds(id), 0f);
            int deeds = MathUtil.FloorToInt(MathUtil.Clamp(deedsF, 0f, HeroRules.MaxDeeds));
            int homeId = HeroKeys.ReadId(state.Memory, HeroKeys.Home(id));
            int civId = HeroKeys.ReadId(state.Memory, HeroKeys.Civ(id));
            var home = homeId >= 0 ? state.Settlements.Get(homeId) : null;
            if (home == null && p != null) home = state.Settlements.Get(p.SettlementId);
            string place = home != null ? home.Name : null;
            if (place == null && civId >= 0) { var c = state.Civilizations.Get(civId); if (c != null) place = "the " + c.Name; }
            if (p != null && p.CivilizationId >= 0) civId = p.CivilizationId;
            bool legendary = HeroRules.IsLegendary(deeds);
            string text = p != null
                ? HeroChronicle.Biography(p.Name, role, place, ageYears, cause, p.Sex, deeds, legendary)
                : HeroChronicle.Biography(null, role, place, -1f, null, null, deeds, legendary);
            Vec2i pos = p != null ? p.Position : (home != null ? home.Center : Vec2i.Invalid);
            ctx.Ops?.AddChronicle(ChronicleCategory.Death, text, civId, home != null ? home.Id : -1, pos, legendary ? 3 : 2);
            HeroKeys.Clear(state.Memory, id);
            _architects.Remove(id);
        }

        private void OnPersonDied(PersonDiedEvent e)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null) return;
            var state = ctx.State;
            if (!state.Memory.TryGetValue(HeroKeys.Role(e.PersonId), out float v)) return;
            if (!HeroKeys.TryDecodeRole(v, out var role)) { HeroKeys.Clear(state.Memory, e.PersonId); return; }
            var p = state.People.Get(e.PersonId);
            Bury(ctx, e.PersonId, role, p, e.AgeYears, e.Cause);
        }

        private void OnBuildingCompleted(BuildingCompletedEvent e)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null || _architects.Count == 0) return;
            var state = ctx.State;
            for (int i = 0; i < _architects.Count; i++)
            {
                var p = state.People.Get(_architects[i]);
                if (p == null || !p.IsAlive || p.SettlementId != e.SettlementId) continue;
                if (!HeroKeys.TryGetRole(state.Memory, p.Id, out var role) || role != HeroRole.Architect) continue;
                AddDeeds(state, p.Id, 1f);
            }
        }

        // =============================================================== wiring

        private void Attach(SimContext ctx)
        {
            if (_bus != null && !ReferenceEquals(_bus, ctx.Events))
            {
                _bus.Unsubscribe(_onDied);
                _bus.Unsubscribe(_onBuilt);
                _bus = null;
            }
            _ctx = ctx;
            if (ctx.Events != null && _bus == null)
            {
                _bus = ctx.Events;
                _bus.Subscribe(_onDied);
                _bus.Subscribe(_onBuilt);
            }
        }

        private void RebuildArchitectCache(WorldState state)
        {
            _architects.Clear();
            HeroKeys.CollectHeroIds(state.Memory, _heroIds);
            for (int i = 0; i < _heroIds.Count; i++)
                if (HeroKeys.TryGetRole(state.Memory, _heroIds[i], out var role) && role == HeroRole.Architect)
                    _architects.Add(_heroIds[i]);
        }

        /// <summary>Unsubscribes from the event bus (e.g. before discarding this instance while the world lives on).</summary>
        public void Detach()
        {
            if (_bus != null) { _bus.Unsubscribe(_onDied); _bus.Unsubscribe(_onBuilt); }
            _bus = null;
            _ctx = null;
        }
    }
}
