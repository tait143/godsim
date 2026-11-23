using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Faith advisor: models a civilization's relationship with the player-god and turns it into explainable,
    /// personality-driven build and staffing advice.
    ///
    /// Perception (event-driven, recorded per settlement in "adv.faith.*" memory):
    ///   * <see cref="GodPowerCastEvent"/> blessings/wrath and <see cref="DisasterEvent"/>s within (cast radius + 5) of a
    ///     settlement (widened for large territories), strength fading with distance and stacking slightly on repeats;
    ///   * destroyed houses of worship (to rebuild them) and newly raised ones (fulfil a vow, write a chronicle line);
    ///   * territory changes (rescan holy <see cref="TileFlags.Sacred"/> tiles).
    ///
    /// Behaviour (every 12 ticks per settlement):
    ///   * gratitude: after a blessing, pious peoples raise a sanctuary "In thanks for the Rain" (temporary strong vote);
    ///   * wrath: pious or fearful peoples build "to appease the angry heavens"; repeated wrath breaks even proud
    ///     peoples' spirit, while impious ones nurse a decaying grudge that votes against worship and priests;
    ///   * anticipation: coverage is projected forward from population growth, a darkening god (negative alignment)
    ///     makes fearful peoples placate pre-emptively, waning devotion is rekindled by the pious;
    ///   * holy sites in territory attract a temple-class sanctuary; lost temples get rebuilt; pious capitals dream of a
    ///     faith wonder;
    ///   * the worship tier escalates through the ages via <see cref="FaithAdvisorLadder"/> (never duplicating capped or
    ///     queued buildings), every vote gated by <see cref="GameDatabase.IsBuildingAvailable"/>;
    ///   * Priest staffing scales with Piety, coverage gap and empty priest slots; urgency is published for UI/learning.
    /// Deterministic (no randomness needed, index iteration only) and allocation-free per tick (reasons are cached).
    /// </summary>
    public sealed class FaithAdvisor : AdvisorSystemBase
    {
        public const string Id = "faith";
        /// <summary>How long a divine act stays fresh in the people's minds.</summary>
        public const int GraceWindowTicks = 144;
        /// <summary>How long a destroyed house of worship is mourned (and rebuilt).</summary>
        public const int RebuildWindowTicks = 192;
        /// <summary>Territory rescan period for holy tiles.</summary>
        public const int SacredRescanTicks = 96;
        /// <summary>Extra tiles around a cast radius within which a settlement feels it.</summary>
        public const int PerceptionMargin = 5;

        public const string ReasonCoverage = "Few places of worship";
        public const string ReasonGrowth = "Worship must keep pace with a growing town";
        public const string ReasonAppease = "Appease the angry heavens";
        public const string ReasonResent = "Resentful of the cruel heavens";
        public const string ReasonHoly = "Honor the holy ground";
        public const string ReasonRevival = "Rekindle waning devotion";
        public const string ReasonPlacate = "Placate a wrathful god before it strikes";
        public const string ReasonPraise = "Praise a generous god";
        public const string ReasonWonder = "Glorify the god with a wonder";
        public const string ThanksPrefix = "In thanks for the ";
        public const string ReasonThanksGeneric = "In thanks for a blessing";

        public override string AdvisorId => Id;
        public override int TickInterval => 12;

        private readonly FaithAdvisorLadder _ladder = new FaithAdvisorLadder();
        private readonly int[] _counts = new int[Counts.Buildings];
        private readonly string[] _thanksReason = new string[Counts.GodPowers];
        private readonly string[] _powerName = new string[Counts.GodPowers];
        private GameDatabase _boundDb;

        private readonly Action<GodPowerCastEvent> _onCast;
        private readonly Action<DisasterEvent> _onDisaster;
        private readonly Action<BuildingPlacedEvent> _onPlaced;
        private readonly Action<BuildingDestroyedEvent> _onDestroyed;
        private readonly Action<TerritoryChangedEvent> _onTerritory;
        private SimContext _ctx;
        private EventBus _bus;

        // per-Advise scratch (reset every call; no allocations)
        private float _sum, _best;
        private string _bestReason;
        private FaithAdvisorMotive _bestMotive;
        private bool _pendingWorship, _templeClass;
        private int _priestSlots, _priestWorkers;

        public FaithAdvisor()
        {
            _onCast = OnCast;
            _onDisaster = OnDisaster;
            _onPlaced = OnPlaced;
            _onDestroyed = OnDestroyed;
            _onTerritory = OnTerritory;
        }

        /// <summary>Read-only access to the worship ladder (UI/debug/tests).</summary>
        public FaithAdvisorLadder Ladder => _ladder;

        public override void Initialize(SimContext ctx)
        {
            if (ctx == null) return;
            Unsubscribe();
            _ctx = ctx;
            Bind(ctx.Db);
            _bus = ctx.Events;
            if (_bus == null) return;
            _bus.Subscribe(_onCast);
            _bus.Subscribe(_onDisaster);
            _bus.Subscribe(_onPlaced);
            _bus.Subscribe(_onDestroyed);
            _bus.Subscribe(_onTerritory);
        }

        private void Unsubscribe()
        {
            if (_bus == null) return;
            _bus.Unsubscribe(_onCast);
            _bus.Unsubscribe(_onDisaster);
            _bus.Unsubscribe(_onPlaced);
            _bus.Unsubscribe(_onDestroyed);
            _bus.Unsubscribe(_onTerritory);
            _bus = null;
        }

        private void Bind(GameDatabase db)
        {
            if (db == null) return;
            _ladder.Bind(db);
            if (ReferenceEquals(db, _boundDb)) return;
            _boundDb = db;
            for (int i = 0; i < Counts.GodPowers; i++)
            {
                var def = i < db.GodPowers.Count ? db.GodPowers[i] : null;
                string name = def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : ((GodPowerType)i).ToString();
                _powerName[i] = name;
                _thanksReason[i] = ThanksPrefix + name;
            }
        }

        // ================================================================== advice
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null || ctx.State == null) return;
            if (_ctx == null) _ctx = ctx;
            Bind(db);

            float now = ctx.Tick;
            float dt = ctx.DeltaTicks < 1 ? 1f : ctx.DeltaTicks;
            RaceDefinition race = (int)civ.Race < db.Races.Count ? db.Races[(int)civ.Race] : null;
            var mem = s.Memory;

            // ---- personality & feelings toward the god
            float piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            float devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f));
            float favor = MathUtil.Clamp(MathUtil.Sanitize(civ.GodFavor), -1f, 1f);
            float align = MathUtil.Clamp(MathUtil.Sanitize(ctx.State.God.Alignment), -1f, 1f);
            float gratitude = AdvisorBoard.GodGratitude(civ);
            float fear = AdvisorBoard.GodFear(civ);
            float cov = MathUtil.Clamp(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Faith]), 0f, 4f);
            int pop = s.Population;

            Survey(ctx, s);

            // ---- divine memory
            float blessTick = M(s, FaithAdvisorKeys.BlessTick, FaithAdvisorKeys.Never);
            float wrathTick = M(s, FaithAdvisorKeys.WrathTick, FaithAdvisorKeys.Never);
            float blessMag = MathUtil.Clamp01(M(s, FaithAdvisorKeys.BlessMag, 0f));
            float wrathMag = MathUtil.Clamp01(M(s, FaithAdvisorKeys.WrathMag, 0f));
            float blessFresh = Freshness(now, blessTick, GraceWindowTicks);
            float wrathFresh = Freshness(now, wrathTick, GraceWindowTicks);
            float offered = M(s, FaithAdvisorKeys.Offered, FaithAdvisorKeys.Never);

            float wrathCount = MathUtil.Clamp(M(s, FaithAdvisorKeys.WrathCount, 0f) * (1f - dt / 192f), 0f, 10f);
            if (wrathCount < 0.01f) mem.Remove(FaithAdvisorKeys.WrathCount); else mem[FaithAdvisorKeys.WrathCount] = wrathCount;

            // Submission: piety, fear, or a spirit broken by repeated wrath.
            float sub = Max(piety, fear, Math.Min(0.65f, 0.18f * wrathCount));
            float appeaseW = MathUtil.SmoothStep(0.35f, 0.6f, sub);
            float resentW = 1f - MathUtil.SmoothStep(0.25f, 0.5f, sub);

            // Resentment: decays over ~2 years; grows on new wrath (impious), eases on new blessings.
            float resent = MathUtil.Clamp01(M(s, FaithAdvisorKeys.Resentment, 0f)) * MathUtil.Clamp01(1f - dt / 200f);
            if (wrathTick > M(s, FaithAdvisorKeys.WrathSeen, FaithAdvisorKeys.Never))
            {
                resent += wrathMag * 0.8f * resentW * (1f - sub);
                mem[FaithAdvisorKeys.WrathSeen] = wrathTick;
            }
            if (blessTick > M(s, FaithAdvisorKeys.BlessSeen, FaithAdvisorKeys.Never))
            {
                resent -= blessMag * (0.25f + 0.25f * (1f - piety));
                mem[FaithAdvisorKeys.BlessSeen] = blessTick;
            }
            resent = MathUtil.Clamp01(resent);
            if (resent < 0.01f) mem.Remove(FaithAdvisorKeys.Resentment); else mem[FaithAdvisorKeys.Resentment] = resent;

            // ---- drives
            float thanks = 0f;
            if (blessFresh > 0f && offered < blessTick)
                thanks = blessFresh * (blessMag * (0.25f + 0.85f * piety) * (0.75f + 0.25f * devotion) + 0.25f * gratitude + 0.1f * Math.Max(0f, favor));

            float appease = 0f;
            if (wrathFresh > 0f && offered < wrathTick)
                appease = wrathFresh * wrathMag * (0.45f + 0.55f * sub) * (1f + 0.5f * Math.Min(1f, wrathCount / 3f)) * appeaseW;

            float negative = resent * (1f - sub) * resentW + wrathFresh * wrathMag * 0.3f * resentW;

            float placate = 0f;
            if (align < -0.25f && wrathFresh <= 0f) placate = (-align - 0.25f) * 0.8f * appeaseW;
            float praise = 0f;
            if (align > 0.4f && favor > 0.15f) praise = (align - 0.4f) * 0.6f * piety;

            // Coverage, projected half a year ahead from recent growth (anticipate dilution before it happens).
            float prevPop = M(s, FaithAdvisorKeys.LastPop, pop);
            float growth = Math.Max(0f, pop - prevPop);
            float projPop = pop + growth * (48f / dt);
            float covProj = pop > 0 && projPop > 0f ? cov * pop / projPop : cov;
            float desired = 0.35f + 0.75f * piety;
            float gap = MathUtil.Clamp01((desired - covProj) / desired);
            float coverage = gap * (0.15f + 0.75f * piety) * (_pendingWorship ? 0.35f : 1f);
            bool growthLed = covProj < cov - 0.05f && cov < desired + 0.1f;
            mem[FaithAdvisorKeys.LastPop] = pop;

            float revival = 0f;
            if (piety > 0.5f && devotion < 0.35f) revival = (0.35f - devotion) * piety * 1.6f * (_pendingWorship ? 0.5f : 1f);

            // Holy sites attract a temple-class sanctuary.
            int sacred = SacredTiles(ctx, s, now);
            float holy = 0f;
            BuildingType holyTarget = BuildingType.TownCenter;
            bool hasHoly = false;
            if (sacred > 0 && !_templeClass)
            {
                hasHoly = _ladder.TryPick(db, civ, s, _counts, race, FaithAdvisorLadder.TempleClassFaith, out holyTarget)
                          || _ladder.TryPick(db, civ, s, _counts, race, 0f, out holyTarget);
                if (hasHoly) holy = (0.25f + 0.55f * piety) * Math.Min(1f, 0.5f + sacred / 8f);
            }

            // Recovery: rebuild a destroyed house of worship.
            float rebuild = 0f;
            BuildingType lostType = BuildingType.TownCenter;
            float lostTick = M(s, FaithAdvisorKeys.LostTick, FaithAdvisorKeys.Never);
            if (Freshness(now, lostTick, RebuildWindowTicks) > 0f)
            {
                int lt = (int)M(s, FaithAdvisorKeys.LostType, -1f);
                if (lt >= 0 && lt < Counts.Buildings && _ladder.IsWorship[lt] && _counts[lt] == 0)
                {
                    lostType = (BuildingType)lt;
                    if (_ladder.CanAdd(db, civ, s, lostType, 0)) rebuild = 0.55f + 0.35f * piety;
                }
            }

            // ---- combine on the ladder pick
            bool hasPick = _ladder.TryPick(db, civ, s, _counts, race, 0f, out BuildingType pick);
            _sum = 0f; _best = 0f; _bestReason = null; _bestMotive = FaithAdvisorMotive.None;
            Add(coverage, growthLed ? ReasonGrowth : ReasonCoverage, growthLed ? FaithAdvisorMotive.Growth : FaithAdvisorMotive.Coverage);
            Add(revival, ReasonRevival, FaithAdvisorMotive.Revival);
            int bk = (int)M(s, FaithAdvisorKeys.BlessKind, 0f);
            Add(thanks, bk >= 0 && bk < Counts.GodPowers ? _thanksReason[bk] : ReasonThanksGeneric, FaithAdvisorMotive.Thanks);
            Add(appease, ReasonAppease, FaithAdvisorMotive.Appease);
            Add(placate, ReasonPlacate, FaithAdvisorMotive.Placate);
            Add(praise, ReasonPraise, FaithAdvisorMotive.Praise);
            float holyDrive = holy, rebuildDrive = rebuild;
            if (hasHoly && hasPick && holyTarget == pick) { Add(holy, ReasonHoly, FaithAdvisorMotive.HolySite); holy = 0f; }
            if (rebuild > 0f && hasPick && lostType == pick) { Add(rebuild, _ladder.RebuildReason[(int)lostType], FaithAdvisorMotive.Rebuild); rebuild = 0f; }

            float net = _sum - negative;
            FaithAdvisorMotive motive = _bestMotive;
            if (net < -0.02f)
            {
                // Resentment wins: vote against every available house of worship and its priests.
                motive = FaithAdvisorMotive.Resentment;
                float v = Math.Max(-1f, net);
                bool first = true;
                var worship = _ladder.Worship;
                for (int k = 0; k < worship.Count; k++)
                {
                    if (!db.IsBuildingAvailable(civ, worship[k])) continue;
                    VoteBuild(s, civ, worship[k], v, first ? ReasonResent : null);
                    first = false;
                }
                var wonders = _ladder.FaithWonders;
                for (int k = 0; k < wonders.Count; k++)
                    if (db.IsBuildingAvailable(civ, wonders[k])) VoteBuild(s, civ, wonders[k], v * 0.5f);
            }
            else
            {
                if (hasPick && net > 0.02f) VoteBuild(s, civ, pick, Math.Min(1f, net), _bestReason);
                if (holy > 0f && hasHoly)
                {
                    VoteBuild(s, civ, holyTarget, holy, ReasonHoly);
                    if (holy > _best) motive = FaithAdvisorMotive.HolySite;
                }
                if (rebuild > 0f)
                {
                    VoteBuild(s, civ, lostType, rebuild, _ladder.RebuildReason[(int)lostType]);
                    if (rebuild > _best) motive = FaithAdvisorMotive.Rebuild;
                }
                if (piety >= 0.6f && (s.IsCapital || civ.CapitalId == s.Id) && _ladder.FaithWonders.Count > 0
                    && _ladder.TryPickWonder(db, civ, s, _counts, out BuildingType wonder) && !CivHas(ctx.State, civ, wonder))
                {
                    float wv = (0.2f + 0.5f * (piety - 0.6f) / 0.4f) * (0.5f + 0.5f * devotion) + 0.3f * thanks;
                    VoteBuild(s, civ, wonder, Math.Min(1f, wv), ReasonWonder);
                    if (motive == FaithAdvisorMotive.None) motive = FaithAdvisorMotive.Wonder;
                }
            }

            // ---- priests
            if (_priestSlots > 0)
            {
                float pv;
                if (net < -0.02f) pv = net * 0.6f;
                else
                {
                    float shortfall = 1f - Math.Min(1f, _priestWorkers / (float)_priestSlots);
                    pv = (0.15f + 0.7f * piety) * (0.3f + 0.7f * gap) * (0.4f + 0.6f * shortfall) + 0.3f * Math.Max(thanks, appease);
                }
                VoteJob(s, civ, JobType.Priest, pv);
            }

            // ---- urgency & explanation
            float u = Max(gap * (0.3f + 0.7f * piety), appease, thanks * 0.7f);
            u = Max(u, rebuildDrive * 0.9f, holyDrive * 0.6f);
            u = Max(u, net < 0f ? -net * 0.8f : 0f, placate * 0.8f);
            Urgency(s, u);
            mem[FaithAdvisorKeys.Motive] = (float)motive;
        }

        private void Add(float drive, string reason, FaithAdvisorMotive motive)
        {
            if (!(drive > 0f)) return;
            _sum += drive;
            if (drive > _best) { _best = drive; _bestReason = reason; _bestMotive = motive; }
        }

        /// <summary>Counts existing (alive) + queued buildings per type, priest slots and worship state.</summary>
        private void Survey(SimContext ctx, Settlement s)
        {
            Array.Clear(_counts, 0, _counts.Length);
            _pendingWorship = false; _templeClass = false; _priestSlots = 0; _priestWorkers = 0;
            var buildings = ctx.State.Buildings;
            var db = ctx.Db;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                _counts[t]++;
                bool religious = _ladder.IsWorship[t] || _ladder.IsFaithWonder[t];
                if (!b.IsComplete) { if (religious) _pendingWorship = true; continue; }
                if (religious && _ladder.FaithOut[t] >= FaithAdvisorLadder.TempleClassFaith) _templeClass = true;
                if (db.TryGetBuilding(b.Type, out var def) && def.Job == JobType.Priest && def.JobSlots > 0)
                {
                    _priestSlots += def.JobSlots;
                    _priestWorkers += b.Workers.Count;
                }
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                _counts[t]++;
                if (_ladder.IsWorship[t] || _ladder.IsFaithWonder[t]) _pendingWorship = true;
            }
        }

        /// <summary>Cached count of Sacred tiles owned by the settlement (rescanned periodically or on territory change).</summary>
        private static int SacredTiles(SimContext ctx, Settlement s, float now)
        {
            float last = M(s, FaithAdvisorKeys.SacredScan, FaithAdvisorKeys.Never);
            if (now - last < SacredRescanTicks && now >= last) return (int)M(s, FaithAdvisorKeys.Sacred, 0f);
            var map = ctx.State.Map;
            int count = 0;
            if (map != null)
            {
                int r = MathUtil.Clamp(s.TerritoryRadius + 1, 1, 64);
                int x0 = Math.Max(0, s.Center.X - r), x1 = Math.Min(map.Width - 1, s.Center.X + r);
                int y0 = Math.Max(0, s.Center.Y - r), y1 = Math.Min(map.Height - 1, s.Center.Y + r);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = map.Index(x, y);
                        if (map.Owner[i] == s.Id && (map.Flags[i] & TileFlags.Sacred) != 0) count++;
                    }
            }
            s.Memory[FaithAdvisorKeys.Sacred] = count;
            s.Memory[FaithAdvisorKeys.SacredScan] = now;
            return count;
        }

        private static bool CivHas(WorldState state, Civilization civ, BuildingType type)
        {
            var sids = civ.SettlementIds;
            for (int i = 0; i < sids.Count; i++)
            {
                var st = state.Settlements.Get(sids[i]);
                if (st == null) continue;
                var ids = st.BuildingIds;
                for (int k = 0; k < ids.Count; k++)
                {
                    var b = state.Buildings.Get(ids[k]);
                    if (b != null && !b.IsDestroyed && b.Type == type) return true;
                }
                for (int k = 0; k < st.BuildQueue.Count; k++)
                    if (st.BuildQueue[k] != null && st.BuildQueue[k].Type == type) return true;
            }
            return false;
        }

        // ================================================================== perception (events)
        private void OnCast(GodPowerCastEvent e)
        {
            if (!e.Success) return;
            int p = (int)e.Power;
            if (p < 0 || p >= Counts.GodPowers) return;
            var db = _ctx?.Db;
            var def = db != null && p < db.GodPowers.Count ? db.GodPowers[p] : null;
            bool wrath = (p >= (int)GodPowerType.Lightning) || (def != null && def.Category == GodPowerCategory.Wrath);
            float mag;
            if (wrath) mag = 1f;
            else if (p <= (int)GodPowerType.Prophet) mag = 1f;
            else if (e.Power == GodPowerType.PlantForest || e.Power == GodPowerType.RevealOre) mag = 0.5f;
            else return; // neutral shaping/creation
            Perceive(e.Target, e.Radius, wrath, p, mag);
        }

        private void OnDisaster(DisasterEvent e)
        {
            float sev = MathUtil.Clamp01(MathUtil.Sanitize(e.Severity));
            Perceive(e.Position, e.Radius, true, FaithAdvisorKeys.DisasterKindOffset + (int)e.Type, 0.4f + 0.5f * sev);
        }

        private void Perceive(Vec2i target, int radius, bool wrath, int kind, float baseMag)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null) return;
            float now = ctx.State.Tick;
            radius = MathUtil.Clamp(radius, 0, 64);
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (!s.IsAlive) continue;
                int reach = radius + PerceptionMargin + (s.TerritoryRadius > 6 ? (s.TerritoryRadius - 6) / 2 : 0);
                long dx = target.X - s.Center.X, dy = target.Y - s.Center.Y;
                long d2 = dx * dx + dy * dy;
                if (d2 > (long)reach * reach) continue;
                float dist = (float)Math.Sqrt(d2);
                float mag = MathUtil.Clamp01(baseMag * (1f - 0.5f * dist / Math.Max(1, reach)));
                Record(s, now, wrath, kind, mag);
            }
        }

        private static void Record(Settlement s, float now, bool wrath, int kind, float mag)
        {
            string tickKey = wrath ? FaithAdvisorKeys.WrathTick : FaithAdvisorKeys.BlessTick;
            string magKey = wrath ? FaithAdvisorKeys.WrathMag : FaithAdvisorKeys.BlessMag;
            string kindKey = wrath ? FaithAdvisorKeys.WrathKind : FaithAdvisorKeys.BlessKind;
            float prevTick = M(s, tickKey, FaithAdvisorKeys.Never);
            float carry = MathUtil.Clamp01(M(s, magKey, 0f)) * Freshness(now, prevTick, GraceWindowTicks);
            bool sameTick = prevTick == now;
            float combined = sameTick ? Math.Max(carry, mag) : Math.Max(mag, carry) + 0.15f * Math.Min(mag, carry);
            s.Memory[magKey] = MathUtil.Clamp01(combined);
            s.Memory[tickKey] = now;
            if (!sameTick || mag >= carry) s.Memory[kindKey] = kind;
            if (wrath && !sameTick) s.Memory[FaithAdvisorKeys.WrathCount] = MathUtil.Clamp(M(s, FaithAdvisorKeys.WrathCount, 0f) + 1f, 0f, 10f);
        }

        private void OnPlaced(BuildingPlacedEvent e)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null) return;
            int t = (int)e.Type;
            if (t < 0 || t >= Counts.Buildings || !(_ladder.IsWorship[t] || _ladder.IsFaithWonder[t])) return;
            var s = ctx.State.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive) return;
            float now = ctx.State.Tick;
            float prevOffered = M(s, FaithAdvisorKeys.Offered, FaithAdvisorKeys.Never);
            float blessTick = M(s, FaithAdvisorKeys.BlessTick, FaithAdvisorKeys.Never);
            float wrathTick = M(s, FaithAdvisorKeys.WrathTick, FaithAdvisorKeys.Never);
            bool thanks = blessTick > prevOffered && Freshness(now, blessTick, GraceWindowTicks) > 0f;
            bool appease = wrathTick > prevOffered && Freshness(now, wrathTick, GraceWindowTicks) > 0f;
            s.Memory[FaithAdvisorKeys.Offered] = now;
            if ((int)M(s, FaithAdvisorKeys.LostType, -1f) == t) s.Memory[FaithAdvisorKeys.LostTick] = FaithAdvisorKeys.Never;
            if (ctx.Ops == null || !(thanks || appease)) return;
            string what = _ladder.Names[t];
            string text;
            if (thanks)
            {
                int k = (int)M(s, FaithAdvisorKeys.BlessKind, 0f);
                string power = k >= 0 && k < Counts.GodPowers ? _powerName[k] : "blessing";
                text = "The people of " + s.Name + " began a " + what + " in thanks for the " + power + ".";
            }
            else text = "The people of " + s.Name + " began a " + what + " to appease the angry heavens.";
            ctx.Ops.AddChronicle(ChronicleCategory.Divine, text, s.CivilizationId, s.Id, e.Origin, 1);
        }

        private void OnDestroyed(BuildingDestroyedEvent e)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null) return;
            int t = (int)e.Type;
            if (t < 0 || t >= Counts.Buildings || !_ladder.IsWorship[t]) return;
            var s = ctx.State.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive || e.Cause == DestroyCause.Demolished) return;
            s.Memory[FaithAdvisorKeys.LostTick] = ctx.State.Tick;
            s.Memory[FaithAdvisorKeys.LostType] = t;
        }

        private void OnTerritory(TerritoryChangedEvent e)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null) return;
            var s = ctx.State.Settlements.Get(e.SettlementId);
            if (s != null && s.IsAlive) s.Memory[FaithAdvisorKeys.SacredScan] = FaithAdvisorKeys.Never;
        }

        // ================================================================== helpers
        private static float M(Settlement s, string key, float fallback) => MathUtil.Sanitize(s.Mem(key, fallback), fallback);

        /// <summary>1 right after the event, fading linearly to 0 over the window.</summary>
        public static float Freshness(float now, float eventTick, int window)
        {
            if (eventTick <= FaithAdvisorKeys.Never + 1f || window <= 0) return 0f;
            float age = now - eventTick;
            if (age < 0f) age = 0f;
            return age >= window ? 0f : 1f - age / window;
        }

        private static float Max(float a, float b, float c) => Math.Max(a, Math.Max(b, c));
    }
}
