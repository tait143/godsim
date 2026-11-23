using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Elven cultural advisor: gives elven settlements their unmistakable character in what they build, how
    /// they staff their groves and how they react to the world. It only votes on the Advisor Council blackboard
    /// (<see cref="AdvisorBoard"/>); the settlement governor turns the accumulated bias into decisions.
    ///
    /// <para><b>Culture notes</b> (the design intent every vote below follows):</para>
    /// <list type="bullet">
    /// <item><b>Forest stewards.</b> Elves count their history in the lifetimes of trees. Each run counts the
    /// <see cref="TileFlags.Trees"/> tiles in the territory and keeps an exponential moving average and a smoothed
    /// trend in settlement memory. A thinning forest brings strong negative votes on every logging building and
    /// on the Woodcutter job. A deep forest allows one camp, and only when wood is genuinely short, because
    /// fallen boughs serve modest needs. Sawmills are always unwelcome.</item>
    /// <item><b>Knowledge and healing first.</b> The Scriptorium, Library and Herbalist come before almost
    /// anything else, followed by every research or health building the catalog offers (found from the data by
    /// its services). Scholars and Healers get high staffing votes; Priests follow the civ's piety.</item>
    /// <item><b>The WorldTree.</b> The work of generations. Once it is available, the capital saves for it:
    /// it votes for the tree, asks for quarriers and weavers to gather its offerings, and asks for storage deep
    /// enough to hold them. It stops when the tree is queued or growing anywhere in the civilization.</item>
    /// <item><b>A light industrial footprint.</b> Factories, foundries, coal mines and oil wells (and any building
    /// that burns or extracts fuel) are refused. Fusion reactors and hydroponics towers are embraced when they
    /// arrive, because they feed and power a city without felling a single tree.</item>
    /// <item><b>Defense in the forest.</b> Watchtowers in the canopy and thorn palisades rather than barracks,
    /// castles or stone walls. Elves are wary (xenophobic) but rarely aggressive, so soldiers are raised only
    /// under real threat.</item>
    /// <item><b>Spacious groves, slow growth.</b> A new hall goes up before anyone has to share a roof, but the
    /// population target rises slowly with tier and age (low fertility, long lives). Groves never sprawl.</item>
    /// <item><b>Serene culture.</b> The theater (song, verse and the long remembering) wins over the arena.
    /// Blood sport is barbarous.</item>
    /// <item><b>Reactions.</b> Foreign axes felling the old trees near a grove bring grief. Happiness falls a
    /// little each run, the watchtowers fill and shrines offer solace. A wildfire, whether a natural disaster,
    /// the god's wrath or burned land in the territory, brings resolve to replant. The elves vote for wells to
    /// water the saplings, for faith and priests, and lay down their axes.</item>
    /// </list>
    ///
    /// Determinism and performance: iterates entity lists by index, uses no randomness, and reuses its
    /// fixed-size buffers (no per-run allocations apart from rare one-off log lines). All values are sanitized
    /// and clamped.
    /// Memory keys (settlement, persisted): <see cref="KeyForestEma"/>, <see cref="KeyForestTrend"/>,
    /// <see cref="KeyForestRatio"/>, <see cref="KeyFireResolve"/>, <see cref="KeyGrief"/>, <see cref="KeyPopTarget"/>,
    /// <see cref="KeyWorldTreeSaving"/>.
    /// </summary>
    public sealed class ElfCultureAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in blackboard keys and AI logs.</summary>
        public const string Id = "race_elf";

        /// <summary>Settlement: exponential moving average of Trees tiles in the territory.</summary>
        public const string KeyForestEma = "ai.elf.forestEma";
        /// <summary>Settlement: smoothed relative forest change per run (negative = shrinking), in [-1, 1].</summary>
        public const string KeyForestTrend = "ai.elf.forestTrend";
        /// <summary>Settlement: share of territory tiles that are forested, in [0, 1].</summary>
        public const string KeyForestRatio = "ai.elf.forestRatio";
        /// <summary>Settlement: resolve to replant after a wildfire, in [0, 1], fades over time.</summary>
        public const string KeyFireResolve = "ai.elf.fireResolve";
        /// <summary>Settlement: grief at foreign logging nearby, in [0, 1].</summary>
        public const string KeyGrief = "ai.elf.grief";
        /// <summary>Settlement: the slow elven population target used for housing votes.</summary>
        public const string KeyPopTarget = "ai.elf.popTarget";
        /// <summary>Capital: how much of the WorldTree's offerings is stockpiled, in [0, 1].</summary>
        public const string KeyWorldTreeSaving = "ai.elf.worldTreeSaving";

        // ------------------------------------------------------------------ tuning
        /// <summary>EMA smoothing of the tree count per run.</summary>
        public const float ForestEmaAlpha = 0.35f;
        /// <summary>Smoothed trend below which the forest counts as shrinking (about 1% per season).</summary>
        public const float ShrinkThreshold = -0.01f;
        /// <summary>Forest share at or above which the forest counts as abundant.</summary>
        public const float AbundantForestRatio = 0.35f;
        /// <summary>Forest share below which the forest is too sparse to cut at all.</summary>
        public const float SparseForestRatio = 0.15f;
        /// <summary>Wood stock below which a deep forest may spare a little wood.</summary>
        public const float ModestWoodNeed = 30f;
        /// <summary>Happiness lost per run at full grief.</summary>
        public const float GriefHappinessPerRun = 0.02f;
        /// <summary>Extra tiles beyond both territories within which foreign logging is felt.</summary>
        public const int GriefRange = 10;
        /// <summary>Fraction of replanting resolve kept per run.</summary>
        public const float FireResolveDecay = 0.7f;

        // ------------------------------------------------------------------ reasons (constant: no per-run allocations)
        private const string RForestShrinking = "the grove is thinning; the axes must rest";
        private const string RForestSparse = "too few trees remain to spare even one";
        private const string RForestModest = "the forest is deep; fallen boughs serve modest needs";
        private const string RForestEnough = "one woodcutter camp is enough for the Elves";
        private const string RSawmill = "the shriek of saws offends the forest";
        private const string RKnowledgeFirst = "knowledge first: the scribes and their scrolls";
        private const string RKnowledge = "the Elves never stop learning";
        private const string RHealingFirst = "healing first: herbs and gentle hands";
        private const string RHealing = "long lives deserve tending";
        private const string RFaith = "the old songs of the grove";
        private const string RFavored = "an Elven tradition";
        private const string RWorldTreePlant = "the WorldTree must take root: a work of generations";
        private const string RWorldTreeSave = "saving for the WorldTree, as our grandparents did";
        private const string RWorldTreeStore = "a deep storehouse for the WorldTree's offerings";
        private const string RWorldTreeWeave = "cloth for the WorldTree's banners";
        private const string RHeavyIndustry = "smoke and soot poison the roots; the Elves will not build it";
        private const string RFuel = "it burns or tears the earth for fuel";
        private const string RPowerGrudging = "a grudging necessity, hidden among the trees";
        private const string RFusion = "power without a single felled tree";
        private const string RHydroponics = "food grown in towers so no forest falls for fields";
        private const string RIndustrialFarm = "monoculture fields where forest once stood";
        private const string RWatchtower = "eyes in the canopy watch for outsiders";
        private const string RPalisade = "a living palisade of thorn and timber";
        private const string RBigMilitary = "the Elves defend from the trees, not from barracks";
        private const string RStoneWall = "stone walls choke the roots";
        private const string RHouseEarly = "a new hall before anyone must share a roof";
        private const string RHouseSpacious = "the groves stay spacious; no need for more halls";
        private const string RHouseSlow = "an Elven grove grows as slowly as its oaks";
        private const string RTheater = "song, verse and the long remembering";
        private const string RArena = "blood sport is barbarous";
        private const string RLoud = "loud revels disturb the forest's sleep";
        private const string RCampfire = "gatherings beneath the stars";
        private const string RGriefWatch = "foreign axes fell the old trees nearby; watch the borders";
        private const string RGriefSolace = "solace for a grieving grove";
        private const string RReplantWell = "water for the saplings: the Elves will replant what burned";
        private const string RReplantFaith = "prayers for the burned forest";
        private const string RReplantAxes = "no axe may touch the forest while it heals";
        private const string LogGrief = "[race_elf] grief: foreign axes fell the old trees near our grove; happiness falls";
        private const string LogFire = "[race_elf] resolve: the forest burned; the Elves swear to replant it";
        private const string LogShrink = "[race_elf] the forest is shrinking; woodcutting is forbidden";

        // ------------------------------------------------------------------ data-driven tables (refilled when the db changes)
        private readonly bool[] _logging = new bool[Counts.Buildings];
        private readonly bool[] _scholarly = new bool[Counts.Buildings];
        private readonly bool[] _healing = new bool[Counts.Buildings];
        private readonly bool[] _faith = new bool[Counts.Buildings];
        private readonly bool[] _fuel = new bool[Counts.Buildings];
        private readonly bool[] _favored = new bool[Counts.Buildings];
        private GameDatabase _tablesDb;

        // ------------------------------------------------------------------ per-run buffers (reused)
        private readonly int[] _have = new int[Counts.Buildings];
        private readonly float[] _vote = new float[Counts.Buildings];
        private readonly float[] _voteMag = new float[Counts.Buildings];
        private readonly string[] _voteReason = new string[Counts.Buildings];
        private readonly float[] _jobVote = new float[Counts.Jobs];

        // ------------------------------------------------------------------ events
        private readonly Action<DisasterEvent> _onDisaster;
        private readonly Action<GodPowerCastEvent> _onPowerCast;
        private EventBus _bus;
        private WorldState _state;

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 24;

        public ElfCultureAdvisor()
        {
            _onDisaster = OnDisaster;
            _onPowerCast = OnPowerCast;
        }

        /// <summary>Subscribes to wildfire events (once per bus) so elven groves remember the fire.</summary>
        public override void Initialize(SimContext ctx)
        {
            if (ctx == null) return;
            _state = ctx.State;
            if (ctx.Db != null) EnsureTables(ctx.Db);
            if (ctx.Events == _bus) return;
            if (_bus != null)
            {
                _bus.Unsubscribe(_onDisaster);
                _bus.Unsubscribe(_onPowerCast);
            }
            _bus = ctx.Events;
            if (_bus != null)
            {
                _bus.Subscribe(_onDisaster);
                _bus.Subscribe(_onPowerCast);
            }
        }

        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (civ.Race != RaceId.Elf) return;
            var db = ctx.Db;
            if (db == null) return;
            EnsureTables(db);
            _state = ctx.State;
            ClearBuffers();
            CountBuildings(ctx.State, db, s, out int housingCap, out int housingPending);

            // ---------------------------------------------------------------- forest survey
            var map = ctx.State.Map;
            int tiles = 0, trees = 0, burned = 0;
            if (map != null) ScanTerritory(map, s, out tiles, out trees, out burned);
            float ratio = tiles > 0 ? trees / (float)tiles : 0f;
            float prevEma = MathUtil.Sanitize(s.Mem(KeyForestEma, -1f), -1f);
            if (prevEma < 0f) prevEma = trees;
            float rawTrend = (trees - prevEma) / Math.Max(prevEma, 4f);
            float trend = MathUtil.Clamp(MathUtil.Sanitize(MathUtil.Lerp(s.Mem(KeyForestTrend), rawTrend, 0.5f)), -1f, 1f);
            bool wasShrinking = s.Mem(KeyForestTrend) < ShrinkThreshold;
            s.Memory[KeyForestEma] = Math.Max(0f, MathUtil.Sanitize(prevEma + (trees - prevEma) * ForestEmaAlpha));
            s.Memory[KeyForestTrend] = trend;
            s.Memory[KeyForestRatio] = MathUtil.Clamp01(ratio);
            bool shrinking = trend < ShrinkThreshold && tiles > 0;
            float shrinkSeverity = shrinking ? MathUtil.Clamp01(-trend * 8f) : 0f;
            if (shrinking && !wasShrinking) s.LogAi(LogShrink);

            float woodStock = s.Stockpile.Get(ResourceType.Wood);
            bool woodShort = woodStock < ModestWoodNeed || (woodStock < ModestWoodNeed * 2.5f && s.Stockpile.Net(ResourceType.Wood) < 0f);

            // ---------------------------------------------------------------- forest stewardship
            for (int i = 0; i < _logging.Length; i++)
            {
                if (!_logging[i]) continue;
                var t = (BuildingType)i;
                bool camp = t == BuildingType.WoodcutterCamp;
                if (shrinking) Add(t, -(0.6f + 0.4f * shrinkSeverity), RForestShrinking);
                else if (ratio < SparseForestRatio) Add(t, -0.5f, RForestSparse);
                else if (camp && ratio >= AbundantForestRatio && woodShort && _have[i] == 0) Add(t, 0.5f, RForestModest);
                else if (camp && _have[i] >= 1) Add(t, -0.3f, RForestEnough);
                if (!camp) Add(t, -0.6f, RSawmill); // anything beyond a camp is industrial logging
            }
            if (shrinking) AddJob(JobType.Woodcutter, -(0.4f + 0.6f * shrinkSeverity));
            else if (ratio < SparseForestRatio) AddJob(JobType.Woodcutter, -0.3f);
            else if (ratio >= AbundantForestRatio) AddJob(JobType.Woodcutter, woodShort ? 0.2f : -0.1f);

            // ---------------------------------------------------------------- knowledge, healing, faith
            float research = MathUtil.Clamp01(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Research]));
            float health = MathUtil.Clamp01(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Health]));
            float piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            for (int i = 0; i < _have.Length; i++)
            {
                var t = (BuildingType)i;
                bool first = t == BuildingType.Scriptorium || t == BuildingType.Library || t == BuildingType.Herbalist;
                if (_scholarly[i])
                {
                    if (_have[i] == 0) Add(t, 0.6f + 0.2f * (1f - research) + (first ? 0.2f : 0f), first ? RKnowledgeFirst : RKnowledge);
                    else Add(t, 0.25f * (1f - research), RKnowledge);
                }
                if (_healing[i])
                {
                    if (_have[i] == 0) Add(t, 0.5f + 0.2f * (1f - health) + (first ? 0.3f : 0f), first ? RHealingFirst : RHealing);
                    else Add(t, 0.2f * (1f - health), RHealing);
                }
                if (_faith[i] && _have[i] == 0) Add(t, 0.2f + 0.4f * piety, RFaith);
                if (_favored[i] && !_logging[i] && _have[i] == 0) Add(t, 0.3f, RFavored);
            }
            AddJob(JobType.Scholar, 0.6f + 0.3f * (1f - research));
            AddJob(JobType.Healer, 0.5f + 0.3f * (1f - health));
            AddJob(JobType.Priest, 0.2f + 0.4f * piety);

            // ---------------------------------------------------------------- the WorldTree
            float worldTreeNeed = AdviseWorldTree(db, civ, s, shrinking, ratio);

            // ---------------------------------------------------------------- light industrial footprint
            for (int i = 0; i < _fuel.Length; i++)
                if (_fuel[i] && !_logging[i]) Add((BuildingType)i, -0.6f, RFuel);
            Add(BuildingType.Factory, -1f, RHeavyIndustry);
            Add(BuildingType.Foundry, -1f, RHeavyIndustry);
            Add(BuildingType.CoalMine, -1f, RHeavyIndustry);
            Add(BuildingType.OilWell, -1f, RHeavyIndustry);
            float power = MathUtil.Clamp01(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Power]));
            if (_fuel[(int)BuildingType.PowerPlant] && power < 0.3f) Add(BuildingType.PowerPlant, 0.3f, RPowerGrudging); // softens the fuel aversion
            Add(BuildingType.IndustrialFarm, -0.6f, RIndustrialFarm);
            Add(BuildingType.FusionReactor, 0.8f, RFusion);
            Add(BuildingType.HydroponicsTower, 0.7f, RHydroponics);
            if (civ.Age >= AgeId.Industrial)
            {
                AddJob(JobType.Miner, -0.3f);
                AddJob(JobType.Laborer, -0.2f);
            }

            // ---------------------------------------------------------------- defense from the trees
            float threat = MathUtil.Clamp01(MathUtil.Sanitize(Math.Max(civ.Mem(GovernorThreatKey), s.Mem(SettlementThreatKey))));
            float xeno = AdvisorBoard.Trait(civ, db, AITrait.Xenophobia);
            float canopy = ratio >= 0.2f ? 1f : 0.7f;
            int tower = (int)BuildingType.Watchtower;
            if (_have[tower] == 0) Add(BuildingType.Watchtower, (0.3f + 0.3f * xeno + 0.4f * threat) * canopy, RWatchtower);
            else Add(BuildingType.Watchtower, 0.3f * threat, RWatchtower);
            if (_have[(int)BuildingType.Palisade] == 0 && (threat > 0.2f || ratio >= 0.2f)) Add(BuildingType.Palisade, (0.3f + 0.4f * threat) * canopy, RPalisade);
            float calm = 1f - threat;
            Add(BuildingType.Barracks, -(0.1f + 0.5f * calm), RBigMilitary);
            Add(BuildingType.Castle, -(0.2f + 0.5f * calm), RBigMilitary);
            Add(BuildingType.MilitaryBase, -(0.1f + 0.4f * calm), RBigMilitary);
            Add(BuildingType.StoneWall, -(0.1f + 0.3f * calm), RStoneWall);
            Add(BuildingType.Armory, -0.3f * calm, RBigMilitary);
            AddJob(JobType.Soldier, threat > 0.5f ? 0.4f * threat : -0.2f);

            // ---------------------------------------------------------------- spacious groves, slow growth
            int pop = s.Population;
            int free = housingCap + housingPending - pop;
            float popTarget = 10f + 6f * (int)s.Tier + 3f * (int)civ.Age;
            s.Memory[KeyPopTarget] = popTarget;
            if (pop < popTarget)
            {
                if (free < 3) Add(BuildingType.House, 0.7f, RHouseEarly);
                else if (free >= 6) Add(BuildingType.House, -0.4f, RHouseSpacious);
            }
            else if (s.Homeless > 0 || free <= 0) Add(BuildingType.House, 0.4f, RHouseEarly);
            else Add(BuildingType.House, -0.3f, RHouseSlow);

            // ---------------------------------------------------------------- serene culture
            float fun = MathUtil.Clamp01(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Entertainment]));
            Add(BuildingType.Theater, _have[(int)BuildingType.Theater] == 0 ? 0.7f : 0.2f * (1f - fun), RTheater);
            Add(BuildingType.Arena, -0.8f, RArena);
            Add(BuildingType.Stadium, -0.5f, RArena);
            Add(BuildingType.Tavern, -0.2f, RLoud);
            if (_have[(int)BuildingType.Campfire] == 0 && fun < 0.5f) Add(BuildingType.Campfire, 0.3f, RCampfire);
            AddJob(JobType.Entertainer, 0.2f);

            // ---------------------------------------------------------------- reactions
            float grief = AdviseGrief(ctx.State, civ, s);
            float resolve = AdviseWildfire(s, tiles, burned);

            Flush(db, civ, s);
            float urgency = Math.Max(Math.Max(shrinkSeverity, resolve), Math.Max(grief, worldTreeNeed * 0.6f));
            Urgency(s, Math.Max(urgency, 0.5f * (1f - research)));
        }

        // ================================================================== WorldTree
        private float AdviseWorldTree(GameDatabase db, Civilization civ, Settlement s, bool shrinking, float forestRatio)
        {
            const BuildingType wt = BuildingType.WorldTree;
            if (!(s.IsCapital || civ.CapitalId == s.Id) || !db.IsBuildingAvailable(civ, wt) || !db.TryGetBuilding(wt, out var def))
                return 0f;
            if (_have[(int)wt] > 0)
            {
                AddJob(JobType.Builder, 0.5f); // every hand sings the WorldTree upward
                if (s.Memory.ContainsKey(KeyWorldTreeSaving)) s.Memory.Remove(KeyWorldTreeSaving);
                return 0f;
            }
            if (CivHasOrQueued(civ, wt))
            {
                if (s.Memory.ContainsKey(KeyWorldTreeSaving)) s.Memory.Remove(KeyWorldTreeSaving);
                return 0f;
            }

            var cost = def.Cost ?? ResourceAmount.None;
            float sum = 0f, maxAmount = 0f;
            int n = 0;
            for (int i = 0; i < cost.Length; i++)
            {
                float amount = MathUtil.Sanitize(cost[i].Amount);
                if (amount <= 0f) continue;
                n++;
                if (amount > maxAmount) maxAmount = amount;
                float have = s.Stockpile.Get(cost[i].Type);
                sum += MathUtil.Clamp01(have / amount);
                if (have >= amount) continue;
                switch (cost[i].Type)
                {
                    case ResourceType.Wood:
                        if (!shrinking && forestRatio >= SparseForestRatio) AddJob(JobType.Woodcutter, 0.3f);
                        break;
                    case ResourceType.Stone: AddJob(JobType.Quarrier, 0.4f); break;
                    case ResourceType.Cloth:
                        AddJob(JobType.Weaver, 0.5f);
                        if (_have[(int)BuildingType.Weaver] == 0) Add(BuildingType.Weaver, 0.4f, RWorldTreeWeave);
                        break;
                    default: AddJob(JobType.Laborer, 0.2f); break;
                }
            }
            float progress = n > 0 ? MathUtil.Clamp01(sum / n) : 1f;
            s.Memory[KeyWorldTreeSaving] = progress;
            if (s.Stockpile.Capacity < maxAmount) Add(BuildingType.Storehouse, 0.5f, RWorldTreeStore);

            bool ready = s.Tier >= def.MinTier && s.Population >= def.MinPopulation && s.Stockpile.CanAfford(cost);
            if (ready) Add(wt, 1f, RWorldTreePlant);
            else Add(wt, 0.6f + 0.4f * progress, RWorldTreeSave);
            return 1f - progress;
        }

        private bool CivHasOrQueued(Civilization civ, BuildingType type)
        {
            var state = _state;
            if (state == null) return false;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var other = state.Settlements.Get(ids[i]);
                if (other == null || !other.IsAlive) continue;
                var q = other.BuildQueue;
                for (int k = 0; k < q.Count; k++) if (q[k] != null && q[k].Type == type) return true;
                var b = other.BuildingIds;
                for (int k = 0; k < b.Count; k++)
                {
                    var bd = state.Buildings.Get(b[k]);
                    if (bd != null && !bd.IsDestroyed && bd.Type == type) return true;
                }
            }
            return false;
        }

        // ================================================================== reactions
        private float AdviseGrief(WorldState state, Civilization civ, Settlement s)
        {
            int axes = 0;
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || !o.IsAlive || o.Id == s.Id || o.CivilizationId == civ.Id) continue;
                int reach = s.TerritoryRadius + o.TerritoryRadius + GriefRange;
                if (Math.Abs(o.Center.X - s.Center.X) > reach || Math.Abs(o.Center.Y - s.Center.Y) > reach) continue;
                var b = o.BuildingIds;
                for (int k = 0; k < b.Count; k++)
                {
                    var bd = state.Buildings.Get(b[k]);
                    if (bd != null && !bd.IsDestroyed && (int)bd.Type < _logging.Length && _logging[(int)bd.Type]) axes++;
                }
            }

            float prev = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(KeyGrief)));
            float target = MathUtil.Clamp01(axes * 0.25f);
            float grief = prev + (target - prev) * 0.5f;
            if (grief < 0.01f)
            {
                if (s.Memory.ContainsKey(KeyGrief)) s.Memory.Remove(KeyGrief);
                return 0f;
            }
            s.Memory[KeyGrief] = grief;
            if (grief < 0.05f) return grief;

            if (prev < 0.05f) s.LogAi(LogGrief);
            float delta = -GriefHappinessPerRun * grief;
            s.Happiness = MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.5f) + delta);
            var residents = s.ResidentIds;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = state.People.Get(residents[i]);
                if (p != null && p.IsAlive) p.Happiness = MathUtil.Clamp01(MathUtil.Sanitize(p.Happiness, 0.5f) + delta);
            }
            Add(BuildingType.Watchtower, 0.4f * grief, RGriefWatch);
            Add(BuildingType.Shrine, 0.3f * grief, RGriefSolace);
            Add(BuildingType.Temple, 0.3f * grief, RGriefSolace);
            AddJob(JobType.Priest, 0.2f * grief);
            return grief;
        }

        private float AdviseWildfire(Settlement s, int tiles, int burned)
        {
            float prev = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(KeyFireResolve)));
            float resolve = prev;
            if (burned > 0)
            {
                float share = burned / (float)Math.Max(1, tiles);
                resolve = Math.Max(resolve, MathUtil.Clamp01(0.4f + share * 6f));
            }
            if (resolve < 0.05f)
            {
                if (s.Memory.ContainsKey(KeyFireResolve)) s.Memory.Remove(KeyFireResolve);
                return 0f;
            }
            if (s.Mem(FireLoggedKey) < 0.5f) { s.LogAi(LogFire); s.Memory[FireLoggedKey] = 1f; }

            Add(BuildingType.Well, 0.5f + 0.5f * resolve, RReplantWell);
            Add(BuildingType.Shrine, 0.4f * resolve, RReplantFaith);
            Add(BuildingType.Temple, 0.4f * resolve, RReplantFaith);
            Add(BuildingType.SpiritTotem, 0.3f * resolve, RReplantFaith);
            for (int i = 0; i < _logging.Length; i++) if (_logging[i]) Add((BuildingType)i, -0.8f * resolve, RReplantAxes);
            AddJob(JobType.Priest, 0.6f * resolve);
            AddJob(JobType.Woodcutter, -resolve);

            float next = resolve * FireResolveDecay;
            if (next < 0.05f)
            {
                s.Memory.Remove(KeyFireResolve);
                s.Memory.Remove(FireLoggedKey);
            }
            else s.Memory[KeyFireResolve] = next;
            return resolve;
        }

        private void OnDisaster(DisasterEvent e)
        {
            if (e.Type == DisasterType.Wildfire) MarkFire(e.Position, e.Radius, e.Severity);
        }

        private void OnPowerCast(GodPowerCastEvent e)
        {
            if (e.Power == GodPowerType.Wildfire && e.Success) MarkFire(e.Target, e.Radius, 1f);
        }

        /// <summary>Records replanting resolve in every elven settlement near a fire (allocation-free, deterministic).</summary>
        private void MarkFire(Vec2i at, int radius, float severity)
        {
            var state = _state;
            if (state == null) return;
            float strength = MathUtil.Clamp01(0.6f + 0.4f * MathUtil.Clamp01(MathUtil.Sanitize(severity)));
            int r = MathUtil.Clamp(radius, 0, 64);
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.IsAlive) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive || civ.Race != RaceId.Elf) continue;
                int reach = s.TerritoryRadius + r + 6;
                if (Math.Abs(at.X - s.Center.X) > reach || Math.Abs(at.Y - s.Center.Y) > reach) continue;
                if (s.Mem(KeyFireResolve) < strength) s.Memory[KeyFireResolve] = strength;
            }
        }

        // ================================================================== helpers
        private const string GovernorThreatKey = "ai.threat";      // civ, written by strategy
        private const string SettlementThreatKey = "ai.gov.threat"; // settlement, written by governor
        private const string FireLoggedKey = "ai.elf.fireLogged";

        private static void ScanTerritory(WorldMap map, Settlement s, out int tiles, out int trees, out int burned)
        {
            int r = MathUtil.Clamp(s.TerritoryRadius, 1, 64), r2 = r * r;
            int cx = s.Center.X, cy = s.Center.Y;
            int owned = 0, ownedTrees = 0, ownedBurned = 0, open = 0, openTrees = 0, openBurned = 0;
            const TileFlags fire = TileFlags.Burning | TileFlags.Scorched;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = map.Index(x, y);
                    if (map.IsWater(i)) continue;
                    var f = map.Flags[i];
                    int o = map.Owner[i];
                    if (o == s.Id)
                    {
                        owned++;
                        if ((f & TileFlags.Trees) != 0) ownedTrees++;
                        if ((f & fire) != 0) ownedBurned++;
                    }
                    else if (o < 0)
                    {
                        open++;
                        if ((f & TileFlags.Trees) != 0) openTrees++;
                        if ((f & fire) != 0) openBurned++;
                    }
                }
            // Territory not claimed yet (fresh camp): use the unclaimed land around the centre instead.
            if (owned > 0) { tiles = owned; trees = ownedTrees; burned = ownedBurned; }
            else { tiles = open; trees = openTrees; burned = openBurned; }
        }

        private void CountBuildings(WorldState state, GameDatabase db, Settlement s, out int housingCap, out int housingPending)
        {
            housingCap = 0;
            housingPending = 0;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t >= _have.Length) continue;
                _have[t]++;
                if (!db.TryGetBuilding(b.Type, out var def) || !def.IsDwelling) continue;
                if (b.IsComplete) housingCap += b.HousingCapacity > 0 ? b.HousingCapacity : def.HousingMax;
                else housingPending += def.HousingMin;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o == null || (int)o.Type >= _have.Length) continue;
                _have[(int)o.Type]++;
                if (db.TryGetBuilding(o.Type, out var def) && def.IsDwelling) housingPending += def.HousingMin;
            }
        }

        private void EnsureTables(GameDatabase db)
        {
            if (ReferenceEquals(db, _tablesDb)) return;
            _tablesDb = db;
            for (int i = 0; i < Counts.Buildings; i++)
            {
                _logging[i] = _scholarly[i] = _healing[i] = _faith[i] = _fuel[i] = _favored[i] = false;
                if (!db.TryGetBuilding((BuildingType)i, out var d)) continue;
                bool woodOut = Touches(d.OutputsPerWorker, ResourceType.Wood);
                _logging[i] = d.Job == JobType.Woodcutter
                    || (woodOut && (d.Category == BuildingCategory.Resource || d.Category == BuildingCategory.Industry))
                    || i == (int)BuildingType.WoodcutterCamp || i == (int)BuildingType.Sawmill;
                _fuel[i] = Touches(d.InputsPerWorker, ResourceType.Fuel) || Touches(d.OutputsPerWorker, ResourceType.Fuel);
                _scholarly[i] = Provides(d.Services, ServiceType.Research) || d.Category == BuildingCategory.Education;
                _healing[i] = Provides(d.Services, ServiceType.Health) || d.Category == BuildingCategory.Health;
                _faith[i] = d.Category == BuildingCategory.Religion && Provides(d.Services, ServiceType.Faith);
            }
            // Explicit cultural anchors, whatever the catalog says.
            _scholarly[(int)BuildingType.Scriptorium] = _scholarly[(int)BuildingType.Library] = true;
            _healing[(int)BuildingType.Herbalist] = true;
            // Wonders are decided elsewhere (WorldTree below, others by the wonder advisors); keep only the Great Library.
            for (int i = 0; i < Counts.Buildings; i++)
                if (db.TryGetBuilding((BuildingType)i, out var d) && d.IsWonder && i != (int)BuildingType.GreatLibrary)
                    _scholarly[i] = _healing[i] = _faith[i] = false;
            _scholarly[(int)BuildingType.WorldTree] = _healing[(int)BuildingType.WorldTree] = _faith[(int)BuildingType.WorldTree] = false;

            var races = db.Races;
            var elf = races != null && (int)RaceId.Elf < races.Count ? races[(int)RaceId.Elf] : null;
            var fav = elf?.FavoredBuildings;
            if (fav != null)
                for (int i = 0; i < fav.Length; i++)
                    if ((int)fav[i] < _favored.Length) _favored[(int)fav[i]] = true;
        }

        private static bool Touches(ResourceAmount[] flows, ResourceType r)
        {
            if (flows == null) return false;
            for (int i = 0; i < flows.Length; i++) if (flows[i].Type == r && flows[i].Amount > 0f) return true;
            return false;
        }

        private static bool Provides(ServiceAmount[] services, ServiceType svc)
        {
            if (services == null) return false;
            for (int i = 0; i < services.Length; i++) if (services[i].Type == svc && services[i].Amount > 0f) return true;
            return false;
        }

        private void ClearBuffers()
        {
            Array.Clear(_have, 0, _have.Length);
            Array.Clear(_vote, 0, _vote.Length);
            Array.Clear(_voteMag, 0, _voteMag.Length);
            Array.Clear(_voteReason, 0, _voteReason.Length);
            Array.Clear(_jobVote, 0, _jobVote.Length);
        }

        private void Add(BuildingType t, float v, string reason)
        {
            int i = (int)t;
            if (i >= _vote.Length) return;
            v = MathUtil.Sanitize(v);
            _vote[i] += v;
            float m = Math.Abs(v);
            if (m > _voteMag[i]) { _voteMag[i] = m; _voteReason[i] = reason; }
        }

        private void AddJob(JobType j, float v)
        {
            int i = (int)j;
            if (i < _jobVote.Length) _jobVote[i] += MathUtil.Sanitize(v);
        }

        /// <summary>Emits one clamped vote per building type (gated by availability) and per job.</summary>
        private void Flush(GameDatabase db, Civilization civ, Settlement s)
        {
            for (int i = 0; i < _vote.Length; i++)
            {
                float v = MathUtil.Clamp(_vote[i], -1f, 1f);
                if (Math.Abs(v) < 0.01f) continue;
                var t = (BuildingType)i;
                if (!db.IsBuildingAvailable(civ, t)) continue;
                VoteBuild(s, civ, t, v, _voteReason[i]);
            }
            for (int j = 1; j < _jobVote.Length; j++)
            {
                float v = MathUtil.Clamp(_jobVote[j], -1f, 1f);
                if (Math.Abs(v) >= 0.01f) VoteJob(s, civ, (JobType)j, v);
            }
        }
    }
}
