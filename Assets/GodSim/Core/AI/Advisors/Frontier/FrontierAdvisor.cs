using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Frontier advisor gives every settlement a geographic role and shapes it accordingly:
    /// <list type="bullet">
    /// <item><b>Frontier</b> (faces another civ): lookout towers and a garrison; walls when the neighbour is hostile or at
    /// war (and earlier when tensions are rising), a border market when relations are friendly.</item>
    /// <item><b>Coastal gateway</b>: fishing, harbors / coastal commerce and the coastal wonder.</item>
    /// <item><b>Wilderness outpost</b>: storage first, local extractors and gentle growth.</item>
    /// <item><b>Resource town</b>: extractors matching its deposits, storage and the first processing step.</item>
    /// <item><b>Heartland</b>: little to say; softly discourages pointless fortifications.</item>
    /// </list>
    /// Roles come from a bounded map scan (<see cref="FrontierScanner"/>) cached in settlement memory and refreshed every
    /// ~4 years (or when the territory grows); diplomacy and army threat are read fresh every run. All building
    /// choices are data-driven (<see cref="FrontierCatalog"/>) and gated by <c>db.IsBuildingAvailable</c>, tier,
    /// population and per-settlement limits. Votes are soft influence through the <see cref="AdvisorBoard"/>.
    /// </summary>
    public sealed class FrontierAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in blackboard keys and logs.</summary>
        public const string Id = "frontier";
        /// <summary>Ticks between map rescans (~4 years).</summary>
        public const int RefreshTicks = SimTime.TicksPerYear * 4;
        /// <summary>Minimum ticks between reason lines in the AI log (unless the role or stance changes).</summary>
        public const int SpeakTicks = SimTime.TicksPerYear * 2;

        public override string AdvisorId => Id;
        public override int TickInterval => 24;

        private readonly FrontierScanner _scanner = new FrontierScanner();
        private readonly int[] _counts = new int[Counts.Buildings];
        private FrontierCatalog _catalog;

        // per-settlement scratch (reset in Advise; no allocations)
        private float _hostility, _friendliness, _threat, _defenseScale, _tradeScale, _extractScale, _growthScale, _defenseDamp;
        private int _foeCiv;
        private DiplomaticStance _foeStance;
        private bool _foeTrade;
        private bool _speak;
        private string _context;

        /// <summary>Last scanner state (for tests/debug UI).</summary>
        public FrontierScanner Scanner => _scanner;

        public override void Initialize(SimContext ctx)
        {
            if (ctx != null && ctx.Db != null) _catalog = new FrontierCatalog(ctx.Db);
        }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (ctx == null || ctx.Db == null || ctx.State == null) return;
            if (_catalog == null || !ReferenceEquals(_catalog.Db, ctx.Db)) _catalog = new FrontierCatalog(ctx.Db);
            long tick = ctx.Tick;

            // ---------------------------------------------------------- role (cached, refreshed every ~4 years)
            int role = FrontierRoles.RoleOf(s);
            int oldRole = role;
            bool roleChanged = false;
            int stagger = (int)((uint)s.Id % 4u) * 24;
            bool haveScan = s.Memory.TryGetValue(FrontierRoles.KeyScanTick, out float lastScan);
            bool radiusChanged = (int)s.Mem(FrontierRoles.KeyRadius, -1f) != s.TerritoryRadius;
            if (role < 0 || !haveScan || radiusChanged || tick - (long)lastScan >= RefreshTicks + stagger || tick < (long)lastScan)
            {
                _scanner.Scan(ctx.State, s);
                role = _scanner.Classify(s, role, out int secondary);
                StoreScan(s, tick, role, secondary);
                roleChanged = role != oldRole;
            }
            int role2 = FrontierRoles.SecondaryRoleOf(s);

            // ---------------------------------------------------------- fresh reads: diplomacy, threat, personality
            EvaluateNeighbours(ctx, civ, s);
            var db = ctx.Db;
            float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float xenophobia = AdvisorBoard.Trait(civ, db, AITrait.Xenophobia);
            float mercantile = AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);
            float diplomacy = AdvisorBoard.Trait(civ, db, AITrait.Diplomacy);
            _defenseScale = 0.75f + 0.5f * Math.Max(aggression, xenophobia);
            _tradeScale = 0.75f + 0.5f * Math.Max(mercantile, diplomacy);
            _extractScale = 0.8f + 0.4f * AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            _growthScale = 0.8f + 0.4f * AdvisorBoard.Trait(civ, db, AITrait.Expansionism);
            float cov = MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Defense]);
            _defenseDamp = cov >= 1.5f ? 0.3f : cov >= 1f ? 0.6f : 1f;

            float prevStance = s.Mem(FrontierRoles.KeyStance, -1f);
            float stanceNow = _foeCiv >= 0 ? (float)_foeStance : -1f;
            bool stanceChanged = prevStance != stanceNow;
            s.Memory[FrontierRoles.KeyStance] = stanceNow;
            s.Memory[FrontierRoles.KeyHostility] = MathUtil.Clamp01(_hostility);
            s.Memory[FrontierRoles.KeyThreat] = MathUtil.Clamp01(_threat);

            float said = s.Mem(FrontierRoles.KeySaid, -1e9f);
            _speak = roleChanged || stanceChanged || tick - (long)said >= SpeakTicks || tick < (long)said;
            if (_speak) s.Memory[FrontierRoles.KeySaid] = tick;
            _context = _speak ? Context(ctx, role, s) : null;
            if (roleChanged) s.LogAi("[" + Id + "] " + s.Name + " is now a " + _context.Substring(0, 1).ToLowerInvariant() + _context.Substring(1));

            CountBuildings(ctx.State, s);

            // ---------------------------------------------------------- votes (primary full strength, secondary 40%)
            float urgency = VoteRole(ctx, civ, s, role, 1f);
            if (role2 >= 0 && role2 != role)
            {
                bool speak = _speak;
                _speak = false;
                VoteRole(ctx, civ, s, role2, 0.4f);
                _speak = speak;
            }
            Urgency(s, urgency);
        }

        protected override void AfterRun(SimContext ctx)
        {
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c == null || !c.IsAlive) continue;
                int front = 0, coast = 0, res = 0;
                for (int k = 0; k < c.SettlementIds.Count; k++)
                {
                    var s = ctx.State.Settlements.Get(c.SettlementIds[k]);
                    if (s == null || !s.IsAlive) continue;
                    int r = FrontierRoles.RoleOf(s);
                    if (r == FrontierRoles.Frontier) front++;
                    else if (r == FrontierRoles.CoastalGateway) coast++;
                    else if (r == FrontierRoles.ResourceTown) res++;
                }
                c.Memory[FrontierRoles.CivKeyFrontiers] = front;
                c.Memory[FrontierRoles.CivKeyGateways] = coast;
                c.Memory[FrontierRoles.CivKeyResourceTowns] = res;
            }
        }

        // ================================================================== role votes
        private float VoteRole(SimContext ctx, Civilization civ, Settlement s, int role, float scale)
        {
            switch (role)
            {
                case FrontierRoles.Frontier: return VoteFrontier(civ, s, scale);
                case FrontierRoles.CoastalGateway: return VoteCoastal(ctx, civ, s, scale);
                case FrontierRoles.WildernessOutpost: return VoteWilderness(civ, s, scale);
                case FrontierRoles.ResourceTown: return VoteResource(civ, s, scale);
                default: return VoteHeartland(civ, s, scale);
            }
        }

        private float VoteFrontier(Civilization civ, Settlement s, float scale)
        {
            float h = MathUtil.Clamp01(_hostility);
            float threat = MathUtil.Clamp01(_threat);
            float hx = MathUtil.Clamp01(h + threat * 0.3f);
            int pop = s.Population;
            float def = _defenseScale * _defenseDamp * scale;

            // Lookouts: always on a border, more when hostile or large.
            int watch = 1 + (hx >= 0.6f ? 1 : 0) + (pop >= 40 ? 1 : 0);
            VoteSlot(civ, s, FrontierCatalog.SlotWatch, watch, (0.35f + 0.55f * hx) * def, true);

            // Walls: hostile / war (or rising tensions), never on a friendly border.
            if (hx >= 0.5f)
            {
                int walls = 2 + (hx >= 0.8f ? 2 : 0) + pop / 30;
                VoteSlot(civ, s, FrontierCatalog.SlotWall, walls, (0.3f + 0.7f * hx) * def, true);
            }
            else if (hx >= 0.3f) VoteSlot(civ, s, FrontierCatalog.SlotWall, 1, 0.25f * def, true);

            // Garrison: needs people to staff it.
            if (pop >= 10)
            {
                int garrison = 1 + (hx >= 0.9f && pop >= 30 ? 1 : 0);
                VoteSlot(civ, s, FrontierCatalog.SlotGarrison, garrison, (0.15f + 0.75f * hx) * def, true);
            }

            // Border trade: friendly neighbours (or a trade agreement); discouraged on a hostile border.
            float friendly = MathUtil.Clamp01(_friendliness);
            if (friendly > 0f && h < 0.6f)
            {
                float tr = (0.5f + 0.3f * friendly + (_foeTrade ? 0.1f : 0f)) * _tradeScale * (1f - h) * scale;
                VoteSlot(civ, s, FrontierCatalog.SlotTrade, pop >= 40 ? 2 : 1, tr, false);
                VoteJob(s, civ, JobType.Merchant, 0.3f * friendly * scale);
            }
            else if (h < 0.5f) VoteSlot(civ, s, FrontierCatalog.SlotTrade, 1, 0.25f * _tradeScale * scale, false);
            else DiscourageSlot(civ, s, FrontierCatalog.SlotTrade, -0.25f * scale);

            VoteJob(s, civ, JobType.Soldier, 0.5f * hx * scale);
            float u = 0.2f + 0.6f * h + 0.3f * threat;
            return MathUtil.Clamp01(u * (0.5f + 0.5f * _defenseDamp) * scale);
        }

        private float VoteCoastal(SimContext ctx, Civilization civ, Settlement s, float scale)
        {
            int pop = s.Population;
            bool hungry = s.Stockpile.Net(ResourceType.Food) < 0f;
            int fish = MathUtil.Clamp(1 + pop / 20, 1, 3);
            VoteSlot(civ, s, FrontierCatalog.SlotFishing, fish, (hungry ? 0.7f : 0.45f) * _growthScale * scale, false);

            bool portAvailable = AnyAvailable(civ, s, FrontierCatalog.SlotPort);
            bool hasPort = SlotCount(FrontierCatalog.SlotPort) > 0;
            VoteSlot(civ, s, FrontierCatalog.SlotPort, pop >= 50 ? 2 : 1, 0.8f * _tradeScale * scale, false);
            if (pop >= 30) VoteWonder(ctx, civ, s, FrontierCatalog.SlotPortWonder, 0.5f * _tradeScale * scale);
            VoteSlot(civ, s, FrontierCatalog.SlotTrade, 1, 0.25f * _tradeScale * scale, false);

            VoteJob(s, civ, JobType.Fisher, 0.3f * scale);
            VoteJob(s, civ, JobType.Merchant, 0.25f * scale);
            float u = 0.25f + (portAvailable && !hasPort ? 0.25f : 0f) + (hungry ? 0.15f : 0f);
            return MathUtil.Clamp01(u * scale);
        }

        private float VoteWilderness(Civilization civ, Settlement s, float scale)
        {
            int pop = s.Population;
            bool noStore = SlotCount(FrontierCatalog.SlotStorage) == 0;
            VoteSlot(civ, s, FrontierCatalog.SlotStorage, pop >= 25 ? 2 : 1, 0.7f * scale, false);

            // Local extractors for what the land offers (trees included), one each.
            for (int k = 0; k < FrontierCatalog.KindCount; k++)
            {
                if (DepositTiles(s, k) <= 0) continue;
                VoteExtractor(civ, s, k, 1, 0.4f * _extractScale * scale);
            }

            // Gentle growth: a roof for the homeless, a little wild food.
            float house = (s.Homeless > 0 ? 0.25f : pop < 12 ? 0.12f : 0f) * _growthScale * scale;
            if (house > 0f) VoteSlot(civ, s, FrontierCatalog.SlotHousing, int.MaxValue, house, false);
            if (DepositTiles(s, FrontierCatalog.KindTrees) > 0) VoteSlot(civ, s, FrontierCatalog.SlotWildFood, 1, 0.2f * _growthScale * scale, false);
            if (s.Mem(FrontierRoles.KeyWater) > 0.05f) VoteSlot(civ, s, FrontierCatalog.SlotFishing, 1, 0.2f * _growthScale * scale, false);
            VoteJob(s, civ, JobType.Woodcutter, 0.2f * scale);
            VoteJob(s, civ, JobType.Hunter, 0.15f * scale);

            float u = 0.2f + (noStore ? 0.25f : 0f) + (s.Homeless > 0 ? 0.15f : 0f);
            return MathUtil.Clamp01(u * scale);
        }

        private float VoteResource(Civilization civ, Settlement s, float scale)
        {
            int pop = s.Population;
            int wanted = 0, missing = 0;
            for (int k = 0; k < FrontierCatalog.KindCount; k++)
            {
                if (k == FrontierCatalog.KindTrees) continue;
                int tiles = DepositTiles(s, k);
                if (tiles <= 0) continue;
                int desired = MathUtil.Clamp(1 + tiles / 8, 1, 3);
                int before = ExtractorCount(k);
                bool voted = VoteExtractor(civ, s, k, desired, 0.75f * _extractScale * scale);
                if (voted) { wanted++; if (before == 0) missing++; }
                if (before > 0) VoteProcessors(civ, s, k, 0.35f * _extractScale * scale);
            }
            VoteSlot(civ, s, FrontierCatalog.SlotStorage, MathUtil.Clamp(1 + pop / 30, 1, 3), 0.5f * scale, false);
            float u = 0.3f + (wanted > 0 ? 0.3f * missing / wanted : 0f);
            return MathUtil.Clamp01(u * scale);
        }

        private float VoteHeartland(Civilization civ, Settlement s, float scale)
        {
            // Safe interior: let commerce breathe, softly discourage fortifications nobody needs.
            VoteSlot(civ, s, FrontierCatalog.SlotTrade, 1, 0.2f * _tradeScale * scale, false);
            if (_threat <= 0f && s.ServiceCoverage[(int)ServiceType.Defense] >= 0.5f) DiscourageSlot(civ, s, FrontierCatalog.SlotWall, -0.15f * scale);
            return MathUtil.Clamp01(0.05f * scale);
        }

        // ================================================================== slot helpers
        /// <summary>Votes for the best buildable type of a slot while the slot has fewer than <paramref name="desired"/> buildings.</summary>
        private bool VoteSlot(Civilization civ, Settlement s, int slot, int desired, float strength, bool preferNewest)
        {
            if (strength <= 0f || _catalog == null) return false;
            var list = _catalog.Slots[slot];
            int have = 0;
            for (int i = 0; i < list.Count; i++) have += _counts[(int)list[i]];
            if (have >= desired) return false;
            int pick = Pick(civ, s, list, preferNewest);
            if (pick < 0) return false;
            Push(s, civ, (BuildingType)pick, have == 0 ? strength : strength * 0.6f);
            return true;
        }

        private void DiscourageSlot(Civilization civ, Settlement s, int slot, float strength)
        {
            if (_catalog == null) return;
            var list = _catalog.Slots[slot];
            for (int i = 0; i < list.Count; i++)
                if (_counts[(int)list[i]] == 0 && IsAvailable(civ, list[i])) VoteBuild(s, civ, list[i], strength);
        }

        private bool VoteExtractor(Civilization civ, Settlement s, int kind, int desired, float strength)
        {
            if (strength <= 0f || _catalog == null) return false;
            var list = _catalog.Extractors[kind];
            int have = 0;
            for (int i = 0; i < list.Count; i++) have += _counts[(int)list[i]];
            if (have >= desired) return false;
            int pick = Pick(civ, s, list, false);
            if (pick < 0) return false;
            Push(s, civ, (BuildingType)pick, have == 0 ? strength : strength * 0.55f);
            var j = _catalog.Db.Building((BuildingType)pick).Job;
            if (j != JobType.None) VoteJob(s, civ, j, strength * 0.45f);
            return true;
        }

        private int ExtractorCount(int kind)
        {
            var list = _catalog.Extractors[kind];
            int have = 0;
            for (int i = 0; i < list.Count; i++) have += _counts[(int)list[i]];
            return have;
        }

        private void VoteProcessors(Civilization civ, Settlement s, int kind, float strength)
        {
            var ext = _catalog.Extractors[kind];
            for (int e = 0; e < ext.Count; e++)
            {
                if (_counts[(int)ext[e]] == 0) continue;
                int res = _catalog.MainOutput(ext[e]);
                if (res < 0) continue;
                var procs = _catalog.Processors[res];
                for (int i = 0; i < procs.Count; i++)
                {
                    if (_counts[(int)procs[i]] > 0) return; // chain already started
                }
                int pick = Pick(civ, s, procs, false);
                if (pick >= 0) Push(s, civ, (BuildingType)pick, strength);
                return;
            }
        }

        private void VoteWonder(SimContext ctx, Civilization civ, Settlement s, int slot, float strength)
        {
            var list = _catalog.Slots[slot];
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (_counts[(int)t] > 0 || !CanAdd(civ, s, t)) continue;
                if (_catalog.Db.Building(t).UniquePerCivilization && CivHas(ctx, civ, t)) continue;
                Push(s, civ, t, strength);
                return;
            }
        }

        private int SlotCount(int slot)
        {
            var list = _catalog.Slots[slot];
            int have = 0;
            for (int i = 0; i < list.Count; i++) have += _counts[(int)list[i]];
            return have;
        }

        private bool AnyAvailable(Civilization civ, Settlement s, int slot)
        {
            var list = _catalog.Slots[slot];
            for (int i = 0; i < list.Count; i++) if (CanAdd(civ, s, list[i])) return true;
            return false;
        }

        private int Pick(Civilization civ, Settlement s, System.Collections.Generic.List<BuildingType> list, bool preferNewest)
        {
            if (preferNewest)
            {
                for (int i = list.Count - 1; i >= 0; i--) if (CanAdd(civ, s, list[i])) return (int)list[i];
            }
            else
            {
                for (int i = 0; i < list.Count; i++) if (CanAdd(civ, s, list[i])) return (int)list[i];
            }
            return -1;
        }

        private bool IsAvailable(Civilization civ, BuildingType t) => _catalog.Db.IsBuildingAvailable(civ, t);

        /// <summary>Availability (single source of truth) plus tier, population and per-settlement limits.</summary>
        private bool CanAdd(Civilization civ, Settlement s, BuildingType t)
        {
            var db = _catalog.Db;
            if (!db.IsBuildingAvailable(civ, t) || !db.TryGetBuilding(t, out var d)) return false;
            if (s.Tier < d.MinTier || s.Population < d.MinPopulation) return false;
            if (d.MaxPerSettlement >= 0 && _counts[(int)t] >= d.MaxPerSettlement) return false;
            return true;
        }

        private static bool CivHas(SimContext ctx, Civilization civ, BuildingType t)
        {
            for (int k = 0; k < civ.SettlementIds.Count; k++)
            {
                var o = ctx.State.Settlements.Get(civ.SettlementIds[k]);
                if (o == null || !o.IsAlive) continue;
                for (int b = 0; b < o.BuildingIds.Count; b++)
                {
                    var bb = ctx.State.Buildings.Get(o.BuildingIds[b]);
                    if (bb != null && !bb.IsDestroyed && bb.Type == t) return true;
                }
                for (int q = 0; q < o.BuildQueue.Count; q++)
                    if (o.BuildQueue[q] != null && o.BuildQueue[q].Type == t) return true;
            }
            return false;
        }

        private void Push(Settlement s, Civilization civ, BuildingType t, float strength)
        {
            float v = MathUtil.Clamp(MathUtil.Sanitize(strength), -1f, 1f);
            string reason = null;
            if (_speak && _context != null && Math.Abs(v * AdvisorBoard.Weight(civ, Id)) >= 0.5f)
                reason = _context + ": " + _catalog.LowerNames[(int)t];
            VoteBuild(s, civ, t, v, reason);
        }

        private static int DepositTiles(Settlement s, int kind) => (int)s.Mem(FrontierRoles.KeyDepositTiles[kind]);

        // ================================================================== state
        private void StoreScan(Settlement s, long tick, int role, int secondary)
        {
            var m = s.Memory;
            m[FrontierRoles.KeyRole] = role;
            m[FrontierRoles.KeyRole2] = secondary;
            m[FrontierRoles.KeyScanTick] = tick;
            m[FrontierRoles.KeyRadius] = s.TerritoryRadius;
            m[FrontierRoles.KeyForeign] = MathUtil.Clamp01(_scanner.ForeignShare);
            m[FrontierRoles.KeyWater] = MathUtil.Clamp01(_scanner.WaterShare);
            m[FrontierRoles.KeyWild] = MathUtil.Clamp01(_scanner.WildShare);
            m[FrontierRoles.KeyProximity] = MathUtil.Clamp01(_scanner.Proximity);
            m[FrontierRoles.KeyDepositMask] = _scanner.DepositMask;
            for (int k = 0; k < FrontierCatalog.KindCount; k++) m[FrontierRoles.KeyDepositTiles[k]] = _scanner.Deposits[k];
            for (int n = 0; n < FrontierScanner.MaxNeighbours; n++) m[FrontierRoles.KeyNeighbour[n]] = _scanner.NeighbourCiv[n];
        }

        private void CountBuildings(WorldState state, Settlement s)
        {
            Array.Clear(_counts, 0, _counts.Length);
            for (int i = 0; i < s.BuildingIds.Count; i++)
            {
                var b = state.Buildings.Get(s.BuildingIds[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t >= 0 && t < _counts.Length) _counts[t]++;
            }
            for (int i = 0; i < s.BuildQueue.Count; i++)
            {
                var o = s.BuildQueue[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t >= 0 && t < _counts.Length) _counts[t]++;
            }
        }

        /// <summary>Reads stance toward every cached neighbour: worst hostility (the foe), best friendliness, army threat.</summary>
        private void EvaluateNeighbours(SimContext ctx, Civilization civ, Settlement s)
        {
            _hostility = 0f; _friendliness = 0f; _threat = 0f;
            _foeCiv = -1; _foeStance = DiplomaticStance.Neutral; _foeTrade = false;
            float xeno = AdvisorBoard.Trait(civ, ctx.Db, AITrait.Xenophobia);
            float bestHost = -1f, bestFriend = 0f;
            for (int n = 0; n < FrontierScanner.MaxNeighbours; n++)
            {
                int other = (int)s.Mem(FrontierRoles.KeyNeighbour[n], -1f);
                if (other < 0 || other == civ.Id) continue;
                var oc = ctx.State.Civilizations.Get(other);
                if (oc == null || !oc.IsAlive) continue;
                var stance = DiplomaticStance.Neutral;
                float opinion = 0f;
                bool trade = false;
                if (ctx.State.Diplomacy.TryGet(civ.Id, other, out var r) && r != null)
                {
                    stance = r.AtWar ? DiplomaticStance.War : r.Stance;
                    opinion = MathUtil.Sanitize(r.Opinion);
                    trade = r.TradeAgreement;
                }
                float h;
                switch (stance)
                {
                    case DiplomaticStance.War: h = 1f; break;
                    case DiplomaticStance.Hostile: h = 0.8f; break;
                    case DiplomaticStance.Friendly: h = 0.12f; break;
                    case DiplomaticStance.Allied: h = 0.02f; break;
                    default: h = 0.35f + MathUtil.Clamp01(-opinion / 100f) * 0.35f + (xeno - 0.5f) * 0.2f; break;
                }
                h = MathUtil.Clamp01(h);
                if (h > bestHost) { bestHost = h; _foeCiv = other; _foeStance = stance; _foeTrade = trade; }
                float f = stance == DiplomaticStance.Allied ? 1f : stance == DiplomaticStance.Friendly ? 0.75f : trade && h < 0.6f ? 0.5f : 0f;
                if (f > bestFriend) bestFriend = f;
            }
            _hostility = bestHost < 0f ? 0f : bestHost;
            _friendliness = bestFriend;

            // Hostile armies closing in (anticipate the siege instead of reacting to it).
            var armies = ctx.State.Diplomacy.Armies.Items;
            float reach = FrontierScanner.MaxRadius + 20f;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (a == null || !a.IsAlive || a.CivilizationId == civ.Id || a.CivilizationId < 0) continue;
                bool enemy = ctx.State.Diplomacy.AreAtWar(civ.Id, a.CivilizationId)
                    || (ctx.State.Diplomacy.TryGet(civ.Id, a.CivilizationId, out var r) && r != null && r.Stance == DiplomaticStance.Hostile);
                if (!enemy) continue;
                float dx = a.Position.X - s.Center.X, dy = a.Position.Y - s.Center.Y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                float t = MathUtil.Clamp01(1f - d / reach);
                if (a.TargetSettlementId == s.Id) t = Math.Max(t, 0.8f);
                if (t > _threat) _threat = t;
            }
        }

        private string Context(SimContext ctx, int role, Settlement s)
        {
            switch (role)
            {
                case FrontierRoles.Frontier:
                {
                    var foe = _foeCiv >= 0 ? ctx.State.Civilizations.Get(_foeCiv) : null;
                    string who = foe == null || string.IsNullOrEmpty(foe.Name) ? "a neighbour" : foe.Name.StartsWith("The ", StringComparison.Ordinal) ? foe.Name : "the " + foe.Name;
                    return "Frontier with " + who + " (" + StanceWord(_foeStance, _hostility) + ")" + (_threat >= 0.3f ? ", enemy army near" : "");
                }
                case FrontierRoles.CoastalGateway: return "Coastal gateway";
                case FrontierRoles.WildernessOutpost: return "Wilderness outpost";
                case FrontierRoles.ResourceTown:
                {
                    string kinds = "";
                    int mask = (int)s.Mem(FrontierRoles.KeyDepositMask);
                    for (int k = 0; k < FrontierCatalog.KindCount; k++)
                    {
                        if (k == FrontierCatalog.KindTrees || (mask & (1 << k)) == 0) continue;
                        kinds = kinds.Length == 0 ? FrontierCatalog.KindNames[k] : kinds + ", " + FrontierCatalog.KindNames[k];
                    }
                    return kinds.Length == 0 ? "Resource town" : "Resource town (" + kinds + ")";
                }
                default: return "Heartland";
            }
        }

        private static string StanceWord(DiplomaticStance stance, float h)
        {
            switch (stance)
            {
                case DiplomaticStance.War: return "at war";
                case DiplomaticStance.Hostile: return "hostile";
                case DiplomaticStance.Friendly: return "friendly";
                case DiplomaticStance.Allied: return "allied";
                default: return h >= 0.5f ? "tense" : "neutral";
            }
        }
    }
}
