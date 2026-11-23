using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The voice of the Thanes: gives Dwarf civilizations an unmistakable culture in what they build, who works,
    /// what they hoard and how they remember. Acts only on settlements whose civ is <see cref="RaceId.Dwarf"/>.
    ///
    /// <para><b>Culture notes.</b> Dwarves are stubborn, industrious master miners and smiths of the hills.</para>
    /// <list type="bullet">
    /// <item><b>Dig first.</b> A dwarf hold opens a quarry, a mine and a gold mine the moment a deposit lies in
    /// its territory, even as a camp; the Smelter follows the Mine and the Blacksmith follows the Smelter.</item>
    /// <item><b>Hoard.</b> Storehouses and Granaries come earlier and more often than for other races; when the
    /// vaults run thin the hold refills them (quarriers, smiths, gold miners); merchants who would sell the hoard
    /// are given less work, and gold is put in a Bank rather than spent.</item>
    /// <item><b>Stone, not timber.</b> A Palisade is a stopgap: the moment Stone Walls are known the hold votes
    /// against more stakes and to wall itself in granite (more strongly where stakes still stand). Watchtowers
    /// crown the hills; a Town wants a Castle.</item>
    /// <item><b>The DeepForge.</b> While the forge is unlit and unqueued, its seat (the capital, or the first hold
    /// beside a mountain) stockpiles exactly the materials its definition costs and votes to build it; once
    /// standing, it is fed ore and smiths.</item>
    /// <item><b>Feast halls.</b> Taverns (a hearth fire before that) keep dwarves happy; Arenas are frowned upon.</item>
    /// <item><b>Industry.</b> In the Industrial age dwarves embrace Foundries, Factories, coal and rail.</item>
    /// <item><b>Grudges.</b> A lost battle, a burned building or a taken hold is written into the Book of Grudges
    /// (civ memory "race.dwarf.grudge.&lt;civId&gt;"). It halves only every 30 years, and while the offender is
    /// near (or at war) the hold keeps military votes elevated: barracks, walls, towers, armories, soldiers.</item>
    /// <item><b>Rebuild in stone.</b> After an earthquake (or a wildfire) the hold spends three years voting for
    /// quarries, quarriers, builders and stone walls, and against timber stakes.</item>
    /// </list>
    /// All decisions are pure functions of world state (no RNG), gated by <see cref="GameDatabase.IsBuildingAvailable"/>,
    /// and use scratch arrays owned by the instance, so the per-run path does not allocate.
    /// </summary>
    public sealed partial class DwarfCultureAdvisor : AdvisorSystemBase
    {
        /// <summary>Stable advisor id used in AdvisorBoard keys and logs.</summary>
        public const string Id = "race_dwarf";

        public override string AdvisorId => Id;
        public override int TickInterval => 24;

        // ------------------------------------------------------------------ reasons (constant strings: no allocation)
        private const string RQuarry = "Stone underfoot and no quarry? Shameful.";
        private const string RMine = "The hills are full of ore. Dig, and dig deep!";
        private const string RGold = "Gold sleeps in the rock. Wake it for the hoard.";
        private const string RCoal = "Black coal in the deep. The furnaces will want it.";
        private const string RSmelter = "Ore without fire is just rock.";
        private const string RSmith = "Every dwarf needs a good hammer.";
        private const string RStore = "A full hold is a happy hold.";
        private const string RStoreFull = "The vaults are near brimming. Carve another hold.";
        private const string RGranary = "Never trust a summer harvest. Fill the granary.";
        private const string RRefill = "The hoard is thinning. Refill the vaults.";
        private const string RBank = "Gold belongs in a vault, not in a purse.";
        private const string RNoStakes = "Timber burns and rots. A dwarf trusts only stone.";
        private const string RReplaceStakes = "Tear down the stakes and raise the granite.";
        private const string RStoneWall = "Thick stone walls, as our fathers built them.";
        private const string RStakes = "Stakes will do until the quarry gives us better.";
        private const string RTower = "From the high hills we see them coming.";
        private const string RCastle = "A hold needs a heart of stone.";
        private const string RForge = "Our ancestors dreamed of the DeepForge. We will light it.";
        private const string RForgeReady = "The stone is hauled and the ale is brewed: light the DeepForge!";
        private const string RForgeHaul = "Haul stone and timber for the DeepForge.";
        private const string RForgeVault = "Our vaults are too small for what the DeepForge demands.";
        private const string RForgeFeed = "The DeepForge hungers for ore.";
        private const string RTavern = "A hall without ale is a tomb.";
        private const string RHearth = "Songs of the deep around the hearth fire.";
        private const string RArena = "Dwarves fight for grudges, not for show.";
        private const string RFoundry = "Coal fire and iron: the deep grows louder.";
        private const string RFactory = "A machine is only smithing at scale.";
        private const string RRail = "Iron rails into the deep halls.";
        private const string RGrudgeBarracks = "A grudge unpaid is a debt unpaid. Muster the axes.";
        private const string RGrudgeWall = "Thicker walls against those who wronged us.";
        private const string RGrudgeTower = "Keep an eye on the old enemy.";
        private const string RGrudgeArmory = "Sharpen the axes. The debt will be paid.";
        private const string RQuake = "The mountain shook. We rebuild in stone.";
        private const string RQuakeStakes = "Timber halls fell like kindling.";

        private static readonly ResourceType[] s_hoard =
        {
            ResourceType.Stone, ResourceType.Ore, ResourceType.Metal, ResourceType.Gold, ResourceType.Tools, ResourceType.Food,
        };

        // ------------------------------------------------------------------ scratch (reused; never reallocated)
        private readonly float[] _bv = new float[Counts.Buildings];
        private readonly float[] _bMag = new float[Counts.Buildings];
        private readonly string[] _bReason = new string[Counts.Buildings];
        private readonly float[] _jv = new float[Counts.Jobs];
        private readonly int[] _have = new int[Counts.Buildings];     // placed (complete or under construction)
        private readonly int[] _complete = new int[Counts.Buildings];
        private readonly int[] _queued = new int[Counts.Buildings];

        // current advise target
        private GameDatabase _db;
        private Civilization _civ;
        private Settlement _s;

        // territory scan
        private int _ore, _stone, _gold, _coal, _hills, _mountain;

        // ------------------------------------------------------------------ ISimSystem
        public override void Initialize(SimContext ctx)
        {
            SubscribeEvents(ctx);
        }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (civ.Race != RaceId.Dwarf || ctx.Db == null || ctx.State == null || ctx.State.Map == null) return;
            if (_ctx != ctx) SubscribeEvents(ctx);
            _db = ctx.Db; _civ = civ; _s = s;

            EnsureCivPass(ctx, civ);
            CountBuildings(ctx.State, s);
            ScanTerritory(ctx.State.Map, s);

            float urgency = AdviseMining();
            urgency = Math.Max(urgency, AdviseHoarding());
            urgency = Math.Max(urgency, AdviseDefenses());
            urgency = Math.Max(urgency, AdviseForge());
            urgency = Math.Max(urgency, AdviseFeasting());
            urgency = Math.Max(urgency, AdviseIndustry());
            AdviseJobs();
            urgency = Math.Max(urgency, AdviseGrudges(ctx.State));
            urgency = Math.Max(urgency, AdviseRebuildInStone(ctx.Tick));

            Flush();
            Urgency(s, urgency);
            _db = null; _civ = null; _s = null;
        }

        // ------------------------------------------------------------------ mining & metal chain
        private float AdviseMining()
        {
            int pop = _s.Population;
            float u = 0f;
            if (_stone > 0) u = Math.Max(u, Want(BuildingType.StoneQuarry, Math.Min(3, 1 + _stone / 8 + pop / 40), 0.9f, RQuarry));
            if (_ore > 0) u = Math.Max(u, Want(BuildingType.Mine, Math.Min(4, 1 + _ore / 6 + pop / 30), 1f, RMine));
            if (_gold > 0) u = Math.Max(u, Want(BuildingType.GoldMine, Math.Min(2, 1 + _gold / 8), 0.85f, RGold));
            if (_coal > 0) u = Math.Max(u, Want(BuildingType.CoalMine, Math.Min(2, 1 + _coal / 8), 0.8f, RCoal));

            // The chain right behind: Mine -> Smelter -> Blacksmith.
            var sp = _s.Stockpile;
            int mines = Planned(BuildingType.Mine);
            bool minePossible = _ore > 0 && CanBuildMore(BuildingType.Mine);
            int wantSmelters = mines > 0 ? Math.Max(1, (mines + 1) / 2) : (sp[ResourceType.Ore] >= 20f || minePossible ? 1 : 0);
            u = Math.Max(u, 0.8f * Want(BuildingType.Smelter, wantSmelters, mines > 0 ? 0.85f : 0.5f, RSmelter));

            int smelters = Planned(BuildingType.Smelter) + _complete[(int)BuildingType.DeepForge];
            int wantSmiths = smelters > 0 ? Math.Min(3, Math.Max(1, (smelters + 1) / 2)) : (sp[ResourceType.Metal] >= 15f ? 1 : 0);
            Want(BuildingType.Blacksmith, wantSmiths, 0.75f, RSmith);
            return u;
        }

        // ------------------------------------------------------------------ hoarding
        private float AdviseHoarding()
        {
            var sp = _s.Stockpile;
            int pop = _s.Population;
            float cap = Math.Max(1f, MathUtil.Sanitize(sp.Capacity, Stockpile.DefaultCapacity));
            float fill = 0f;
            for (int i = 0; i < s_hoard.Length; i++) fill = Math.Max(fill, sp[s_hoard[i]] / cap);
            fill = MathUtil.Clamp01(MathUtil.Sanitize(fill));

            float u = 0.5f * Want(BuildingType.Storehouse, Math.Min(8, 1 + pop / 18), 0.6f, RStore);
            if (fill > 0.5f) { Add(BuildingType.Storehouse, 0.3f + (fill - 0.5f) * 1.4f, RStoreFull); u = Math.Max(u, fill * 0.7f); }

            float foodFill = MathUtil.Clamp01(sp[ResourceType.Food] / cap);
            Want(BuildingType.Granary, pop >= 6 ? Math.Min(4, 1 + pop / 40) : 0, 0.55f, RGranary);
            if (foodFill > 0.55f) Add(BuildingType.Granary, 0.25f + (foodFill - 0.55f), RGranary);

            // Keep the hoard high: refill what runs thin.
            if (Thin(sp, ResourceType.Stone, cap)) { if (_stone > 0) Add(BuildingType.StoneQuarry, 0.4f, RRefill); _jv[(int)JobType.Quarrier] += 0.3f; }
            if (Thin(sp, ResourceType.Metal, cap) && (Planned(BuildingType.Mine) > 0 || sp[ResourceType.Ore] > 0f)) { Add(BuildingType.Smelter, 0.35f, RRefill); _jv[(int)JobType.Smith] += 0.2f; }
            if (Thin(sp, ResourceType.Gold, cap) && _gold > 0) { Add(BuildingType.GoldMine, 0.4f, RRefill); _jv[(int)JobType.Miner] += 0.2f; }
            if (sp[ResourceType.Gold] > 0.2f * cap) Want(BuildingType.Bank, 1, 0.4f, RBank);
            return u;
        }

        private static bool Thin(Stockpile sp, ResourceType r, float cap) => sp[r] < 0.3f * cap && sp.Net(r) <= 0.01f;

        // ------------------------------------------------------------------ stone defenses
        private float AdviseDefenses()
        {
            int pop = _s.Population;
            int palisades = Planned(BuildingType.Palisade);
            float u = 0f;
            if (IsAvailable(BuildingType.StoneWall))
            {
                Add(BuildingType.Palisade, -0.8f, RNoStakes);
                int max = MaxPer(BuildingType.StoneWall, 24);
                int want = Math.Min(max, 4 + 3 * (int)_s.Tier + 2 * palisades);
                u = 0.6f * Want(BuildingType.StoneWall, want, palisades > 0 ? 0.95f : 0.7f, palisades > 0 ? RReplaceStakes : RStoneWall);
            }
            else if (pop >= 12)
            {
                Want(BuildingType.Palisade, Math.Min(3, 1 + pop / 30), 0.55f, RStakes);
            }
            if (_hills >= 3) Want(BuildingType.Watchtower, Math.Min(3, 1 + _hills / 25), 0.6f, RTower);
            if (_s.Tier >= SettlementTier.Town) Want(BuildingType.Castle, 1, 0.5f, RCastle);
            return u;
        }

        // ------------------------------------------------------------------ the DeepForge
        private float AdviseForge()
        {
            const BuildingType forge = BuildingType.DeepForge;
            if (!_db.IsBuildingAvailable(_civ, forge) || !_db.TryGetBuilding(forge, out var def)) return 0f;

            int state = (int)_civ.Mem(DwarfCultureKeys.ForgeState);
            int local = _complete[(int)forge] > 0 ? 3 : _have[(int)forge] > 0 ? 2 : _queued[(int)forge] > 0 ? 1 : 0;
            if (local > state) state = local;

            if (state >= 3)
            {
                if (_complete[(int)forge] > 0)
                {
                    if (_ore > 0) Add(BuildingType.Mine, 0.4f, RForgeFeed);
                    _jv[(int)JobType.Smith] += 0.4f;
                    _jv[(int)JobType.Miner] += 0.3f;
                }
                return 0.1f;
            }

            int seat = (int)_civ.Mem(DwarfCultureKeys.ForgeSeat, _civ.CapitalId);
            if (_s.Id != seat) return 0f;
            if (state == 2) { _jv[(int)JobType.Builder] += 0.6f; return 0.5f; }
            if (state == 1) return 0.4f; // queued: the construction pipeline takes it from here
            if ((int)_s.Tier < (int)def.MinTier - 1) return 0f; // a camp does not dream of wonders yet

            // Unqueued ambition: stockpile exactly what the definition costs, at the seat.
            var sp = _s.Stockpile;
            float cap = Math.Max(1f, MathUtil.Sanitize(sp.Capacity, Stockpile.DefaultCapacity));
            float readiness = 1f;
            var cost = def.Cost;
            if (cost != null)
            {
                for (int i = 0; i < cost.Length; i++)
                {
                    float need = MathUtil.Sanitize(cost[i].Amount);
                    if (need <= 0f) continue;
                    float got = sp[cost[i].Type];
                    if (cap < need) Add(BuildingType.Storehouse, 0.8f, RForgeVault);
                    if (got >= need) continue;
                    float ratio = MathUtil.Clamp01(got / need);
                    readiness = Math.Min(readiness, ratio);
                    VoteProducer(cost[i].Type, 0.35f + 0.5f * (1f - ratio), RForgeHaul);
                }
            }
            if (CanBuildMore(forge)) Add(forge, readiness >= 1f ? 1f : 0.5f + 0.4f * readiness, readiness >= 1f ? RForgeReady : RForge);
            return 0.3f + 0.5f * readiness;
        }

        /// <summary>Data-driven: votes the canonical producer of a resource plus that building's own job.</summary>
        private void VoteProducer(ResourceType r, float v, string reason)
        {
            BuildingType b;
            switch (r)
            {
                case ResourceType.Stone: if (_stone <= 0) { _jv[(int)JobType.Quarrier] += v * 0.5f; return; } b = BuildingType.StoneQuarry; break;
                case ResourceType.Ore: if (_ore <= 0) return; b = BuildingType.Mine; break;
                case ResourceType.Gold: if (_gold <= 0) return; b = BuildingType.GoldMine; break;
                case ResourceType.Fuel: if (_coal <= 0) return; b = BuildingType.CoalMine; break;
                case ResourceType.Wood: b = BuildingType.WoodcutterCamp; break;
                case ResourceType.Metal: b = BuildingType.Smelter; break;
                case ResourceType.Tools: b = BuildingType.Blacksmith; break;
                case ResourceType.Cloth: b = BuildingType.Weaver; break;
                case ResourceType.Food: b = BuildingType.Farm; break;
                default: return;
            }
            Add(b, Planned(b) == 0 ? v : v * 0.5f, reason);
            if (_db.TryGetBuilding(b, out var d) && d.Job != JobType.None) _jv[(int)d.Job] += v * 0.6f;
        }

        // ------------------------------------------------------------------ feasting halls
        private float AdviseFeasting()
        {
            int pop = _s.Population;
            float happy = MathUtil.Clamp01(MathUtil.Sanitize(_s.Happiness, 0.6f));
            float u = 0f;
            if (IsAvailable(BuildingType.Tavern))
            {
                if (pop >= 8) u = (1f - happy) * Want(BuildingType.Tavern, Math.Min(4, Math.Max(1, pop / 50)), happy < 0.6f ? 1f : 0.7f, RTavern);
            }
            else if (pop >= 4) Want(BuildingType.Campfire, 1, 0.4f, RHearth);
            Add(BuildingType.Arena, -0.35f, RArena);
            if (happy < 0.5f) _jv[(int)JobType.Entertainer] += 0.3f;
            return u;
        }

        // ------------------------------------------------------------------ industrial readiness
        private float AdviseIndustry()
        {
            if (_civ.Age < AgeId.Industrial) return 0f;
            float u = 0.5f * Want(BuildingType.Foundry, Math.Min(3, 1 + Planned(BuildingType.Mine) / 2), 0.85f, RFoundry);
            Want(BuildingType.Factory, Math.Min(3, 1 + _s.Population / 60), 0.65f, RFactory);
            Want(BuildingType.RailStation, 1, 0.35f, RRail);
            _jv[(int)JobType.Engineer] += 0.4f;
            return u;
        }

        // ------------------------------------------------------------------ job flavour
        private void AdviseJobs()
        {
            float ind = AdvisorBoard.Trait(_civ, _db, AITrait.Industriousness);
            float k = 0.6f + 0.4f * ind;
            _jv[(int)JobType.Miner] += 0.6f * k;
            _jv[(int)JobType.Smith] += 0.5f * k;
            _jv[(int)JobType.Quarrier] += 0.5f * k;
            _jv[(int)JobType.Merchant] -= 0.45f; // sell the hoard? over my beard
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Votes when fewer than <paramref name="want"/> are planned; returns an urgency contribution.</summary>
        private float Want(BuildingType t, int want, float strength, string reason)
        {
            if (want <= 0 || !CanBuildMore(t)) return 0f;
            int have = Planned(t);
            if (have >= want) return 0f;
            float lack = (want - have) / (float)want;
            float v = strength * (have == 0 ? 1f : 0.45f + 0.55f * lack);
            Add(t, v, reason);
            return have == 0 ? strength : v * 0.6f;
        }

        private void Add(BuildingType t, float v, string reason)
        {
            int i = (int)t;
            if (i < 0 || i >= _bv.Length || v == 0f) return;
            _bv[i] += MathUtil.Sanitize(v);
            float mag = Math.Abs(v);
            if (mag > _bMag[i]) { _bMag[i] = mag; _bReason[i] = reason; }
        }

        private int Planned(BuildingType t) => _have[(int)t] + _queued[(int)t];

        private bool IsAvailable(BuildingType t)
        {
            if (!_db.IsBuildingAvailable(_civ, t) || !_db.TryGetBuilding(t, out var d)) return false;
            return _s.Tier >= d.MinTier;
        }

        private bool CanBuildMore(BuildingType t)
        {
            if (!IsAvailable(t)) return false;
            var d = _db.Buildings[(int)t];
            return d.MaxPerSettlement < 0 || Planned(t) < d.MaxPerSettlement;
        }

        private int MaxPer(BuildingType t, int fallback)
        {
            if (!_db.TryGetBuilding(t, out var d) || d.MaxPerSettlement < 0) return fallback;
            return d.MaxPerSettlement;
        }

        private void Flush()
        {
            for (int i = 0; i < _bv.Length; i++)
            {
                float v = _bv[i];
                string reason = _bReason[i];
                _bv[i] = 0f; _bMag[i] = 0f; _bReason[i] = null;
                if (v == 0f) continue;
                var t = (BuildingType)i;
                if (v > 0f ? !CanBuildMore(t) : !IsAvailable(t)) continue;
                VoteBuild(_s, _civ, t, MathUtil.Clamp(v, -1f, 1f), reason);
            }
            for (int j = 1; j < _jv.Length; j++)
            {
                float v = _jv[j];
                _jv[j] = 0f;
                if (v != 0f) VoteJob(_s, _civ, (JobType)j, MathUtil.Clamp(v, -1f, 1f));
            }
            _jv[0] = 0f;
        }

        private void CountBuildings(WorldState state, Settlement s)
        {
            Array.Clear(_have, 0, _have.Length);
            Array.Clear(_complete, 0, _complete.Length);
            Array.Clear(_queued, 0, _queued.Length);
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= _have.Length) continue;
                _have[t]++;
                if (b.IsComplete) _complete[t]++;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                if (q[i] == null) continue;
                int t = (int)q[i].Type;
                if (t >= 0 && t < _queued.Length) _queued[t]++;
            }
        }

        /// <summary>One pass over the hold's reach: deposits it can work (own or unclaimed tiles), hills, nearby mountains.</summary>
        private void ScanTerritory(WorldMap map, Settlement s)
        {
            _ore = _stone = _gold = _coal = _hills = _mountain = 0;
            int r = MathUtil.Clamp(s.TerritoryRadius + 2, 3, 24);
            int rm = r + 3;
            var c = s.Center;
            for (int y = c.Y - rm; y <= c.Y + rm; y++)
            {
                for (int x = c.X - rm; x <= c.X + rm; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if (map.Biomes[i] == Biome.Mountain) _mountain++;
                    if (Math.Abs(x - c.X) > r || Math.Abs(y - c.Y) > r) continue;
                    int owner = map.Owner[i];
                    if (owner >= 0 && owner != s.Id) continue;
                    if (map.Biomes[i] == Biome.Hills) _hills++;
                    var f = map.Flags[i];
                    if ((f & TileFlags.OreDeposit) != 0) _ore++;
                    if ((f & TileFlags.StoneDeposit) != 0) _stone++;
                    if ((f & TileFlags.GoldDeposit) != 0) _gold++;
                    if ((f & TileFlags.CoalDeposit) != 0) _coal++;
                }
            }
        }

        private static int CountMountains(WorldMap map, Settlement s)
        {
            int rm = MathUtil.Clamp(s.TerritoryRadius + 2, 3, 24) + 3;
            return map.CountBiomeInRadius(s.Center, rm, Biome.Mountain);
        }
    }
}
