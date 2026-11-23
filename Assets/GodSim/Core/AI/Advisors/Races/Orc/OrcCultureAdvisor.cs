using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Orc culture advisor: gives Orc settlements their own way of building, working and reacting. It only
    /// votes on the Advisor Council (<see cref="AdvisorBoard"/>) and never queues orders itself.
    ///
    /// <para><b>Culture notes.</b> Orcs are hardy, fast-breeding warrior clans of the steppes, tundra and broken
    /// hills. Status comes from strength and from keeping the clan fed. That leads to eight habits, each kept to
    /// what a real clan could afford:</para>
    /// <list type="bullet">
    /// <item><b>Warrior heart.</b> A war-hall (Barracks) as soon as the camp can field one, an Armory the moment
    /// the forges allow it, and steady votes for the metal chain (Mine, Smelter) and smiths so the weapon racks
    /// fill up. Bronze-age camps already lean on ore and metal for spearheads.</item>
    /// <item><b>Arena culture.</b> Happiness comes from loud feasts and blood sport: Campfire, then Arena, then Tavern
    /// (and the Stadium in modern times). Orcs have no patience for painted players, so the Theater gets a vote against it.</item>
    /// <item><b>War Idol ambition.</b> While the racial wonder is available and nobody has started it, the capital
    /// hoards its cost (read from the definition). It votes for the best available producer of each missing resource
    /// and for storage when the hoard would not fit. Once the idol is queued, the capital stops hoarding and puts
    /// builders on it instead.</item>
    /// <item><b>Hunter-herders.</b> In Dawn and Bronze the clans feed themselves with hunting lodges and pastures, and
    /// fields are frowned upon unless the camp is going hungry. In Iron and Classical times herds still come first. From
    /// the Medieval age on, the horde adopts farms like everyone else.</item>
    /// <item><b>Sprawling camps.</b> House votes scale with the race's fertility, the homeless count and expansionism.
    /// The camp keeps building ahead of its next generation, and slows down when there is plenty of free housing.</item>
    /// <item><b>Raiding posture.</b> A weaker hostile or at-war settlement nearby raises soldier and smith staffing and
    /// votes for war-halls and armories ("The weak invite the strong"). A stronger hostile clan nearby is respected:
    /// the camp holds its palisade instead of picking a fight. An empty belly dampens the war-lust.</item>
    /// <item><b>Honoring wrath.</b> When the god casts wrath near an Orc settlement, the clan reads it as the
    /// sky-hunter's just anger. This "dread" adds devotion votes for totems, shrines, temples, priests and the War Idol.
    /// It never adds resentment, so religion never gets a negative vote. Dread fades slowly. The civ-level
    /// <see cref="AdvisorBoard.GodFear"/> feeds the same response.</item>
    /// <item><b>Pragmatic defenses.</b> A cheap Palisade early. Stone walls are scorned until the Medieval age, unless
    /// the threat is serious.</item>
    /// </list>
    ///
    /// <para>Acts only on settlements whose civilization's race is <see cref="RaceId.Orc"/>. Every positive vote is
    /// gated by <see cref="GameDatabase.IsBuildingAvailable"/>, by the tier and population minimums, and by the
    /// per-settlement and per-civ limits. The advisor is deterministic (no randomness, settlements are iterated by
    /// index) and allocation-free per run (reused arrays, constant reason strings).</para>
    ///
    /// <para>Blackboard: reads settlement <c>econ.foodDays</c> and <c>pop.freeHousing</c>, and civ <c>ai.threat</c>,
    /// <c>ai.wars</c>, <c>ai.posture</c> and <c>ai.god.fear</c>. Writes settlement <see cref="DreadKey"/> (0..1) and
    /// <see cref="RaidKey"/> (0..1).</para>
    /// </summary>
    public sealed class OrcCultureAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id (memory keys, logs, learned weight).</summary>
        public const string Id = "race_orc";

        /// <summary>Settlement memory: fearful devotion after a nearby god wrath, 0..1 (fades over time).</summary>
        public const string DreadKey = "ai.orc.dread";
        /// <summary>Settlement memory: raid fervour toward the nearest weaker hostile neighbour at the last run, 0..1.</summary>
        public const string RaidKey = "ai.orc.raid";

        // Keys written by other modules (read only; duplicated as strings so this file depends only on Contracts).
        private const string FoodDaysKey = "econ.foodDays";
        private const string FreeHousingKey = "pop.freeHousing";
        private const string CivThreatKey = "ai.threat";
        private const string CivWarsKey = "ai.wars";
        private const string CivPostureKey = "ai.posture";
        private const int PostureMilitarist = 2;

        // ------------------------------------------------------------------ tuning
        /// <summary>Tiles beyond a settlement's territory within which a wrath cast is felt.</summary>
        public const int WrathAwareness = 10;
        /// <summary>Dread added by a full-strength wrath cast right on the settlement.</summary>
        public const float DreadPerCast = 0.7f;
        /// <summary>Dread lost per tick (linear and deterministic). About 2.5 years from full dread to calm.</summary>
        public const float DreadFadePerTick = 0.004f;
        /// <summary>Raid scouting range in tiles (centre to centre).</summary>
        public const int RaidRange = 36;
        /// <summary>A neighbour is "weak" when its local power is below ours times this.</summary>
        public const float WeakRatio = 0.75f;
        /// <summary>A neighbour is "stronger" when its local power is above ours times this.</summary>
        public const float StrongRatio = 1.25f;
        /// <summary>Largest total build vote this advisor casts for one type in a run.</summary>
        public const float MaxVotePerType = 1.5f;
        private const float BaselineFertility = 0.25f;

        // ------------------------------------------------------------------ reasons (constant strings: no allocations)
        private const string RBarracksFirst = "A clan without a war-hall is prey; raise the Barracks";
        private const string RBarracksMore = "The young bloods outgrow the old war-hall";
        private const string RArmoryFirst = "Steel before bread: the forge-masters want an Armory";
        private const string RArmoryMore = "The weapon racks stand half-empty";
        private const string RSmelter = "The Armory hungers for metal";
        private const string RMine = "Dig deeper: the smelters starve for ore";
        private const string RBronzeChain = "Bronze spearheads for the hunt and the raid";
        private const string RCampfire = "Loud feasts around the fire keep the clan proud";
        private const string RArenaFirst = "Blood and sand: honor is won in the Arena";
        private const string RArenaMore = "The crowds are restless for champions";
        private const string RTavern = "Mead-halls where warbands boast of their scars";
        private const string RStadium = "The old arena, grown loud with the ages";
        private const string RTheater = "Painted players? Orcs would rather watch a real fight";
        private const string RIdol = "The sky-hunter must see our War Idol from the clouds";
        private const string RIdolStore = "A bigger hoard for the War Idol's timber and stone";
        private const string RIdolHoard = "Hoard materials for the War Idol";
        private const string RLodgeEarly = "The hunt feeds the clan: lodges before furrows";
        private const string RPastureEarly = "Herds walk with the clan; fields do not";
        private const string RFarmScorn = "Grubbing in the dirt is for softer peoples";
        private const string RFarmHungry = "Hungry bellies overrule pride: break the sod";
        private const string RFarmLate = "Even a horde must plough to stay fed";
        private const string RLodgeMid = "The old hunting grounds still feed us";
        private const string RPastureMid = "Fat herds, warm hides, full bellies";
        private const string RHouse = "Orc litters grow fast: more lodges before the snows";
        private const string RHouseHomeless = "Warriors sleeping in the mud: raise more lodges";
        private const string RRaidBarracks = "The weak invite the strong";
        private const string RRaidArmory = "Sharpen the axes: a soft neighbour lies close";
        private const string RRaidTower = "Eyes on the weakling's herds";
        private const string RRespect = "A stronger clan prowls near: respect it and hold the stakes";
        private const string RTemple = "The sky-hunter's anger is just: bring him offerings";
        private const string RShrine = "Fearful devotion: bones and blood for the angry sky";
        private const string RIdolDread = "Raise the idol higher, so the god sees we are worthy";
        private const string RCathedral = "A great hall for the god who smote us";
        private const string RPalisade = "Sharpened stakes: cheap, quick, and enough";
        private const string RWallScorn = "Stone walls are for those who fear the fight";
        private const string RWallLate = "The clans finally stack stone against siege engines";
        private const string RFavored = "The old clan ways favour it";

        // ------------------------------------------------------------------ reusable scratch (no per-run allocations)
        private readonly float[] _build = new float[Counts.Buildings];
        private readonly float[] _reasonMag = new float[Counts.Buildings];
        private readonly string[] _reason = new string[Counts.Buildings];
        private readonly float[] _job = new float[Counts.Jobs];
        private readonly int[] _have = new int[Counts.Buildings];     // placed (complete or under construction)
        private readonly int[] _complete = new int[Counts.Buildings];
        private readonly int[] _queued = new int[Counts.Buildings];

        private SimContext _ctx;
        private EventBus _bus;
        private Action<GodPowerCastEvent> _onCast;

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 24;

        /// <summary>Subscribes to god power casts so that wrath near Orc settlements turns into fearful devotion.</summary>
        public override void Initialize(SimContext ctx)
        {
            _ctx = ctx;
            var bus = ctx != null ? ctx.Events : null;
            if (bus == _bus) return;
            if (_bus != null && _onCast != null) _bus.Unsubscribe(_onCast);
            _bus = bus;
            if (_bus == null) return;
            if (_onCast == null) _onCast = OnGodPowerCast;
            _bus.Subscribe(_onCast);
        }

        // ================================================================== wrath -> dread
        private void OnGodPowerCast(GodPowerCastEvent e)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null || !e.Success) return;
            if (!IsWrath(ctx.Db, e.Power)) return;
            RegisterWrath(ctx, e.Power, e.Target, e.Radius);
        }

        /// <summary>
        /// Records a god wrath cast at <paramref name="target"/>. Orc settlements within the radius, plus their
        /// territory and <see cref="WrathAwareness"/> tiles, gain dread scaled by proximity and by the power's mana
        /// cost (a lightning bolt frightens less than a meteor). Public so composition and tests can feed casts
        /// that bypass the event bus.
        /// </summary>
        public static void RegisterWrath(SimContext ctx, GodPowerType power, Vec2i target, int radius)
        {
            if (ctx == null || ctx.State == null || !target.IsValid) return;
            float severity = WrathSeverity(ctx.Db, power);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                var civ = ctx.State.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive || civ.Race != RaceId.Orc) continue;
                int range = Math.Max(0, radius) + Math.Max(0, s.TerritoryRadius) + WrathAwareness;
                int dSq = s.Center.DistanceSqTo(target);
                if (dSq > range * range) continue;
                float proximity = 1f - 0.5f * ((float)Math.Sqrt(dSq) / Math.Max(1, range)); // 1 at ground zero .. 0.5 at the edge
                float dread = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(DreadKey)) + DreadPerCast * severity * proximity);
                s.Memory[DreadKey] = dread;
            }
        }

        private static bool IsWrath(GameDatabase db, GodPowerType power)
        {
            int i = (int)power;
            if (db != null && i >= 0 && i < db.GodPowers.Count && db.GodPowers[i] != null)
                return db.GodPowers[i].Category == GodPowerCategory.Wrath;
            return power >= GodPowerType.Lightning; // enum groups wrath powers last
        }

        private static float WrathSeverity(GameDatabase db, GodPowerType power)
        {
            int i = (int)power;
            if (db != null && i >= 0 && i < db.GodPowers.Count && db.GodPowers[i] != null)
                return MathUtil.Clamp(0.35f + MathUtil.Sanitize(db.GodPowers[i].ManaCost) / 150f, 0.35f, 1f);
            return 0.6f;
        }

        // ================================================================== advice
        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (civ.Race != RaceId.Orc) return; // other races are never touched
            var db = ctx.Db;
            var state = ctx.State;
            if (db == null || state == null) return;
            RaceDefinition race = (int)civ.Race < db.Races.Count ? db.Races[(int)civ.Race] : null;

            Clear();
            Survey(state, s);

            int pop = s.Population;
            float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float expansion = AdvisorBoard.Trait(civ, db, AITrait.Expansionism);
            float threat = MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(CivThreatKey)));
            float foodNeed = FoodNeed(s, race, pop);

            WarriorHeart(db, civ, s, pop, aggression);
            ArenaCulture(db, civ, s, pop);
            float hoard = WarIdolAmbition(db, state, civ, s, pop);
            HunterHerder(db, civ, s, pop, foodNeed);
            float housing = SprawlingHousing(db, civ, s, pop, race, expansion);
            float raid = RaidingPosture(db, state, civ, s, pop, aggression, foodNeed);
            float devotion = HonorWrath(db, state, civ, s, pop, ctx.DeltaTicks);
            PragmaticDefenses(db, civ, s, pop, threat);
            Favored(db, civ, s, pop, race);

            Emit(civ, s);
            Urgency(s, MathUtil.Clamp01(Math.Max(Math.Max(raid, devotion), Math.Max(housing * 0.8f, Math.Max(hoard * 0.6f, threat * 0.7f)))));
        }

        // ------------------------------------------------------------------ warrior heart
        private void WarriorHeart(GameDatabase db, Civilization civ, Settlement s, int pop, float aggression)
        {
            int barracks = Count(BuildingType.Barracks);
            if (Can(db, civ, s, pop, BuildingType.Barracks))
            {
                if (barracks == 0) Add(BuildingType.Barracks, 0.9f, RBarracksFirst);
                else if (pop >= 80) Add(BuildingType.Barracks, 0.35f, RBarracksMore);
            }
            if (_complete[(int)BuildingType.Barracks] > 0) AddJob(JobType.Soldier, 0.15f + 0.2f * aggression);

            var stock = s.Stockpile;
            int armories = Count(BuildingType.Armory);
            float weapons = stock.Get(ResourceType.Weapons);
            float weaponTarget = 10f + pop * 0.25f + s.Soldiers;
            bool weaponsShort = weapons < weaponTarget && stock.Net(ResourceType.Weapons) <= 0.02f;
            if (Can(db, civ, s, pop, BuildingType.Armory))
            {
                if (armories == 0) Add(BuildingType.Armory, 0.9f, RArmoryFirst);
                else if (weaponsShort && armories < 1 + pop / 60) Add(BuildingType.Armory, 0.4f, RArmoryMore);
            }
            if (_complete[(int)BuildingType.Armory] > 0 && weaponsShort) AddJob(JobType.Smith, 0.4f);

            // The metal chain that feeds the Armory (or bronze spearheads before it exists).
            bool armoryAge = armories > 0 || db.IsBuildingAvailable(civ, BuildingType.Armory);
            float metal = stock.Get(ResourceType.Metal);
            float ore = stock.Get(ResourceType.Ore);
            float metalTarget = 20f + armories * 15f;
            int smelters = Count(BuildingType.Smelter);
            if (armoryAge)
            {
                if (metal < metalTarget && stock.Net(ResourceType.Metal) <= 0.02f && Can(db, civ, s, pop, BuildingType.Smelter) && smelters < 1 + armories)
                    Add(BuildingType.Smelter, smelters == 0 ? 0.55f : 0.35f, RSmelter);
                if (smelters > 0 && ore < 20f && stock.Net(ResourceType.Ore) <= 0.02f && Can(db, civ, s, pop, BuildingType.Mine) && Count(BuildingType.Mine) < smelters + 1)
                    Add(BuildingType.Mine, 0.45f, RMine);
                if (metal < metalTarget) AddJob(JobType.Miner, 0.25f);
            }
            else if (civ.Age >= AgeId.Bronze)
            {
                if (smelters == 0 && Can(db, civ, s, pop, BuildingType.Smelter)) Add(BuildingType.Smelter, 0.25f, RBronzeChain);
                if (Count(BuildingType.Mine) == 0 && Can(db, civ, s, pop, BuildingType.Mine)) Add(BuildingType.Mine, 0.25f, RBronzeChain);
            }
        }

        // ------------------------------------------------------------------ arena culture
        private void ArenaCulture(GameDatabase db, Civilization civ, Settlement s, int pop)
        {
            float need = 1f - MathUtil.Clamp01(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Entertainment]));
            if (Can(db, civ, s, pop, BuildingType.Campfire) && Count(BuildingType.Campfire) < 1 + pop / 40 && need > 0.2f)
                Add(BuildingType.Campfire, 0.2f + 0.5f * need, RCampfire);
            if (Can(db, civ, s, pop, BuildingType.Arena))
            {
                int arenas = Count(BuildingType.Arena);
                if (arenas == 0) Add(BuildingType.Arena, 0.8f, RArenaFirst);
                else if (need > 0.3f && arenas < 1 + pop / 150) Add(BuildingType.Arena, 0.4f, RArenaMore);
            }
            if (Can(db, civ, s, pop, BuildingType.Tavern) && Count(BuildingType.Tavern) < 1 + pop / 60)
                Add(BuildingType.Tavern, 0.35f + 0.3f * need, RTavern);
            if (Can(db, civ, s, pop, BuildingType.Stadium) && Count(BuildingType.Stadium) == 0)
                Add(BuildingType.Stadium, 0.4f, RStadium);
            if (db.IsBuildingAvailable(civ, BuildingType.Theater)) Add(BuildingType.Theater, -0.6f, RTheater);
            if (need > 0.3f) AddJob(JobType.Entertainer, 0.2f * need);
        }

        // ------------------------------------------------------------------ war idol ambition
        /// <summary>Returns how much of the idol's cost is still missing at the capital (0..1), or 0 when not hoarding.</summary>
        private float WarIdolAmbition(GameDatabase db, WorldState state, Civilization civ, Settlement s, int pop)
        {
            const BuildingType idol = BuildingType.WarIdol;
            if (!db.IsBuildingAvailable(civ, idol) || !db.TryGetBuilding(idol, out var def)) return 0f;
            if (_queued[(int)idol] > 0 || (_have[(int)idol] > 0 && _complete[(int)idol] == 0))
            {
                AddJob(JobType.Builder, 0.35f); // the idol is going up here: every hand to the ropes
                return 0f;
            }
            bool capital = s.IsCapital || civ.CapitalId == s.Id;
            if (!capital || s.Tier < def.MinTier) return 0f;
            if (CivHasOrQueued(state, civ, idol)) return 0f; // built, or queued somewhere: no hoarding

            Add(idol, 0.6f, RIdol);
            var stock = s.Stockpile;
            var cost = def.Cost ?? ResourceAmount.None;
            float missingTotal = 0f;
            bool overflow = false;
            for (int i = 0; i < cost.Length; i++)
            {
                float amount = MathUtil.Sanitize(cost[i].Amount);
                if (amount <= 0f) continue;
                if (stock.Capacity < amount) overflow = true;
                float have = stock.Get(cost[i].Type);
                if (have >= amount) continue;
                float shortfall = MathUtil.Clamp01(1f - have / amount);
                missingTotal = Math.Max(missingTotal, shortfall);
                int producer = BestProducer(db, civ, s, pop, cost[i].Type, out JobType job);
                if (producer >= 0) Add((BuildingType)producer, 0.25f + 0.25f * shortfall, RIdolHoard);
                if (job != JobType.None) AddJob(job, 0.2f + 0.3f * shortfall);
            }
            if (overflow && Can(db, civ, s, pop, BuildingType.Storehouse)) Add(BuildingType.Storehouse, 0.4f, RIdolStore);
            return missingTotal;
        }

        /// <summary>
        /// Finds the buildable, non-wonder building with the highest per-worker output of a resource (no inputs), and
        /// the job of the best available producer overall (so existing producers get staffed even at their limit).
        /// </summary>
        private int BestProducer(GameDatabase db, Civilization civ, Settlement s, int pop, ResourceType res, out JobType job)
        {
            job = JobType.None;
            int best = -1;
            float bestBuild = 0f, bestAny = 0f;
            var defs = db.Buildings;
            for (int t = 0; t < defs.Count; t++)
            {
                var d = defs[t];
                if (d == null || d.IsWonder || !d.HasJobs || d.OutputsPerWorker == null) continue;
                if (d.InputsPerWorker != null && d.InputsPerWorker.Length > 0) continue;
                float o = 0f;
                for (int k = 0; k < d.OutputsPerWorker.Length; k++)
                    if (d.OutputsPerWorker[k].Type == res) o += MathUtil.Sanitize(d.OutputsPerWorker[k].Amount);
                if (o <= 0f || !db.IsBuildingAvailable(civ, (BuildingType)t)) continue;
                if (o > bestAny) { bestAny = o; job = d.Job; }
                if (o > bestBuild && Can(db, civ, s, pop, (BuildingType)t)) { bestBuild = o; best = t; }
            }
            return best;
        }

        // ------------------------------------------------------------------ hunter-herders
        private void HunterHerder(GameDatabase db, Civilization civ, Settlement s, int pop, float foodNeed)
        {
            float push = 0.6f + 0.6f * foodNeed;
            bool lodgeOk = Can(db, civ, s, pop, BuildingType.HuntingLodge) && Count(BuildingType.HuntingLodge) < 1 + pop / 12;
            bool pastureOk = Can(db, civ, s, pop, BuildingType.Pasture) && Count(BuildingType.Pasture) < 1 + pop / 10;
            bool farmAvail = db.IsBuildingAvailable(civ, BuildingType.Farm);

            if (civ.Age <= AgeId.Bronze)
            {
                if (lodgeOk) Add(BuildingType.HuntingLodge, 0.55f * push, RLodgeEarly);
                if (pastureOk) Add(BuildingType.Pasture, 0.55f * push, RPastureEarly);
                if (farmAvail)
                {
                    bool starving = foodNeed > 0.7f && !lodgeOk && !pastureOk;
                    if (starving) Add(BuildingType.Farm, 0.3f, RFarmHungry);
                    else if (foodNeed < 0.85f) Add(BuildingType.Farm, -0.4f, RFarmScorn);
                }
            }
            else if (civ.Age <= AgeId.Classical)
            {
                if (lodgeOk) Add(BuildingType.HuntingLodge, 0.3f * push, RLodgeMid);
                if (pastureOk) Add(BuildingType.Pasture, 0.4f * push, RPastureMid);
                if (farmAvail && foodNeed > 0.5f) Add(BuildingType.Farm, 0.2f * foodNeed, RFarmHungry);
            }
            else
            {
                if (Can(db, civ, s, pop, BuildingType.Farm)) Add(BuildingType.Farm, 0.15f + 0.3f * foodNeed, RFarmLate);
                if (pastureOk) Add(BuildingType.Pasture, 0.25f * push, RPastureMid);
                if (lodgeOk) Add(BuildingType.HuntingLodge, 0.15f * push, RLodgeMid);
            }
            if (_complete[(int)BuildingType.HuntingLodge] > 0) AddJob(JobType.Hunter, 0.25f + 0.2f * foodNeed);
            if (_complete[(int)BuildingType.Pasture] > 0) AddJob(JobType.Herder, 0.25f + 0.2f * foodNeed);
        }

        // ------------------------------------------------------------------ sprawling housing
        private float SprawlingHousing(GameDatabase db, Civilization civ, Settlement s, int pop, RaceDefinition race, float expansion)
        {
            if (!Can(db, civ, s, pop, BuildingType.House)) return 0f;
            float fertility = race != null ? MathUtil.Clamp(MathUtil.Sanitize(race.FertilityPerYear, BaselineFertility) / BaselineFertility, 0.5f, 2f) : 1f;
            int homeless = Math.Max(0, s.Homeless);
            float free = s.Mem(FreeHousingKey, -1f);
            float v = 0.2f * fertility * (0.6f + 0.6f * expansion);
            if (free >= 0f && free >= 6f + pop * 0.1f) v *= 0.3f; // plenty of empty bunks
            float pressure = MathUtil.Clamp01(homeless / 6f);
            v += 0.6f * pressure;
            Add(BuildingType.House, MathUtil.Clamp(v, 0f, 1f), homeless > 0 ? RHouseHomeless : RHouse);
            if (_queued[(int)BuildingType.House] > 0) AddJob(JobType.Builder, 0.15f + 0.2f * pressure);
            return pressure;
        }

        // ------------------------------------------------------------------ raiding posture
        private float RaidingPosture(GameDatabase db, WorldState state, Civilization civ, Settlement s, int pop, float aggression, float foodNeed)
        {
            float ours = LocalPower(s, civ);
            float nearestWeakSq = float.MaxValue;
            bool stronger = false;
            int rangeSq = RaidRange * RaidRange;
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || !o.IsAlive || o.CivilizationId == civ.Id || o.CivilizationId < 0) continue;
                int dSq = s.Center.DistanceSqTo(o.Center);
                if (dSq > rangeSq) continue;
                if (!state.Diplomacy.TryGet(civ.Id, o.CivilizationId, out var rel)) continue;
                if (!rel.AtWar && rel.Stance > DiplomaticStance.Hostile) continue;
                var other = state.Civilizations.Get(o.CivilizationId);
                if (other == null || !other.IsAlive) continue;
                float theirs = LocalPower(o, other);
                if (theirs < ours * WeakRatio) { if (dSq < nearestWeakSq) nearestWeakSq = dSq; }
                else if (theirs > ours * StrongRatio) stronger = true;
            }

            float hunger = 1f - 0.6f * foodNeed; // an empty belly swings no axe
            float fervour = 0f;
            if (nearestWeakSq < float.MaxValue)
            {
                float closeness = 1f - (float)Math.Sqrt(nearestWeakSq) / RaidRange;
                fervour = MathUtil.Clamp01((0.5f + 0.5f * closeness) * (0.6f + 0.4f * aggression) * hunger);
                AddJob(JobType.Soldier, 0.75f * fervour);
                AddJob(JobType.Smith, 0.3f * fervour);
                if (Can(db, civ, s, pop, BuildingType.Barracks)) Add(BuildingType.Barracks, 0.6f * fervour, RRaidBarracks);
                if (Can(db, civ, s, pop, BuildingType.Armory)) Add(BuildingType.Armory, 0.4f * fervour, RRaidArmory);
                if (Can(db, civ, s, pop, BuildingType.Watchtower) && Count(BuildingType.Watchtower) == 0) Add(BuildingType.Watchtower, 0.25f * fervour, RRaidTower);
                s.Memory[RaidKey] = fervour;
            }
            else s.Memory.Remove(RaidKey);

            if (stronger)
            {
                AddJob(JobType.Soldier, 0.3f);
                if (Can(db, civ, s, pop, BuildingType.Palisade) && Count(BuildingType.Palisade) == 0) Add(BuildingType.Palisade, 0.6f, RRespect);
                if (Can(db, civ, s, pop, BuildingType.Watchtower) && Count(BuildingType.Watchtower) < 2) Add(BuildingType.Watchtower, 0.35f, RRespect);
            }
            if (civ.Mem(CivWarsKey) > 0f) AddJob(JobType.Soldier, 0.3f * hunger);
            if ((int)civ.Mem(CivPostureKey) == PostureMilitarist) AddJob(JobType.Soldier, 0.2f * hunger);
            return fervour;
        }

        /// <summary>Rough local fighting power: defense, soldiers (scaled by the civ's military modifier) and able bodies.</summary>
        private static float LocalPower(Settlement s, Civilization civ)
        {
            float mil = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Military, 1f), 0.1f, 5f);
            float p = Math.Max(0f, MathUtil.Sanitize(s.DefenseStrength)) + Math.Max(0, s.Soldiers) * 2f * mil + s.Population * 0.15f;
            return Math.Max(0.01f, p);
        }

        // ------------------------------------------------------------------ honoring wrath
        private float HonorWrath(GameDatabase db, WorldState state, Civilization civ, Settlement s, int pop, int deltaTicks)
        {
            float dread = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(DreadKey)));
            float devotion = Math.Max(dread, 0.8f * AdvisorBoard.GodFear(civ));
            if (devotion > 0.05f)
            {
                // Fearful devotion only: religious votes are always positive, never resentment.
                if (Can(db, civ, s, pop, BuildingType.SpiritTotem)) Add(BuildingType.SpiritTotem, 0.6f * devotion, RShrine);
                if (Can(db, civ, s, pop, BuildingType.Shrine)) Add(BuildingType.Shrine, 0.6f * devotion, RShrine);
                if (Can(db, civ, s, pop, BuildingType.Temple)) Add(BuildingType.Temple, 0.8f * devotion, RTemple);
                if (Can(db, civ, s, pop, BuildingType.Cathedral)) Add(BuildingType.Cathedral, 0.4f * devotion, RCathedral);
                AddJob(JobType.Priest, 0.6f * devotion);
                if (db.IsBuildingAvailable(civ, BuildingType.WarIdol) && (s.IsCapital || civ.CapitalId == s.Id)
                    && _queued[(int)BuildingType.WarIdol] == 0 && !CivHasOrQueued(state, civ, BuildingType.WarIdol))
                    Add(BuildingType.WarIdol, 0.7f * devotion, RIdolDread);
            }
            if (s.Memory.ContainsKey(DreadKey))
            {
                float faded = dread - DreadFadePerTick * Math.Max(1, deltaTicks);
                if (faded <= 0.02f) s.Memory.Remove(DreadKey); else s.Memory[DreadKey] = faded;
            }
            return devotion;
        }

        // ------------------------------------------------------------------ pragmatic defenses
        private void PragmaticDefenses(GameDatabase db, Civilization civ, Settlement s, int pop, float threat)
        {
            if (Can(db, civ, s, pop, BuildingType.Palisade) && Count(BuildingType.Palisade) == 0 && (pop >= 15 || threat > 0.3f))
                Add(BuildingType.Palisade, 0.45f + 0.35f * threat, RPalisade);
            if (db.IsBuildingAvailable(civ, BuildingType.StoneWall))
            {
                if (civ.Age < AgeId.Medieval)
                {
                    if (threat < 0.6f) Add(BuildingType.StoneWall, -0.35f, RWallScorn);
                }
                else if (Can(db, civ, s, pop, BuildingType.StoneWall) && Count(BuildingType.StoneWall) == 0)
                    Add(BuildingType.StoneWall, 0.15f + 0.45f * threat, RWallLate);
            }
        }

        // ------------------------------------------------------------------ data-driven favourites
        private void Favored(GameDatabase db, Civilization civ, Settlement s, int pop, RaceDefinition race)
        {
            var fav = race != null ? race.FavoredBuildings : null;
            if (fav == null) return;
            for (int i = 0; i < fav.Length; i++)
                if (Can(db, civ, s, pop, fav[i])) Add(fav[i], 0.15f, RFavored);
        }

        // ================================================================== helpers
        private static float FoodNeed(Settlement s, RaceDefinition race, int pop)
        {
            if (pop <= 0) return 0f;
            float days = s.Mem(FoodDaysKey, -1f);
            if (days < 0f)
            {
                float eat = Math.Max(0.001f, pop * (race != null ? MathUtil.Clamp(MathUtil.Sanitize(race.FoodPerTick, 0.05f), 0.001f, 1f) : 0.05f));
                days = s.Stockpile.Get(ResourceType.Food) / eat;
            }
            return 1f - MathUtil.Clamp01(MathUtil.Sanitize(days) / 60f);
        }

        /// <summary>Available now, tier and population allow it, and the per-settlement / per-civ limits are not reached.</summary>
        private bool Can(GameDatabase db, Civilization civ, Settlement s, int pop, BuildingType type)
        {
            if (!db.IsBuildingAvailable(civ, type) || !db.TryGetBuilding(type, out var def)) return false;
            if (s.Tier < def.MinTier || pop < def.MinPopulation) return false;
            if (def.MaxPerSettlement >= 0 && Count(type) >= def.MaxPerSettlement) return false;
            return true;
        }

        private int Count(BuildingType t) => _have[(int)t] + _queued[(int)t];

        private static bool CivHasOrQueued(WorldState state, Civilization civ, BuildingType type)
        {
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var o = state.Settlements.Get(ids[i]);
                if (o == null || !o.IsAlive) continue;
                for (int b = 0; b < o.BuildingIds.Count; b++)
                {
                    var bld = state.Buildings.Get(o.BuildingIds[b]);
                    if (bld != null && !bld.IsDestroyed && bld.Type == type) return true;
                }
                for (int q = 0; q < o.BuildQueue.Count; q++)
                    if (o.BuildQueue[q] != null && o.BuildQueue[q].Type == type) return true;
            }
            return false;
        }

        private void Survey(WorldState state, Settlement s)
        {
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

        private void Clear()
        {
            Array.Clear(_build, 0, _build.Length);
            Array.Clear(_reasonMag, 0, _reasonMag.Length);
            Array.Clear(_reason, 0, _reason.Length);
            Array.Clear(_job, 0, _job.Length);
            Array.Clear(_have, 0, _have.Length);
            Array.Clear(_complete, 0, _complete.Length);
            Array.Clear(_queued, 0, _queued.Length);
        }

        private void Add(BuildingType t, float v, string reason)
        {
            int i = (int)t;
            v = MathUtil.Sanitize(v);
            if (v == 0f) return;
            _build[i] += v;
            float mag = Math.Abs(v);
            if (mag > _reasonMag[i]) { _reasonMag[i] = mag; _reason[i] = reason; }
        }

        private void AddJob(JobType j, float v) => _job[(int)j] += MathUtil.Sanitize(v);

        private void Emit(Civilization civ, Settlement s)
        {
            for (int t = 0; t < _build.Length; t++)
            {
                float v = MathUtil.Clamp(_build[t], -MaxVotePerType, MaxVotePerType);
                if (Math.Abs(v) < 0.05f) continue;
                float first = MathUtil.Clamp(v, -1f, 1f);
                VoteBuild(s, civ, (BuildingType)t, first, _reason[t]);
                float rest = v - first;
                if (rest != 0f) VoteBuild(s, civ, (BuildingType)t, rest);
            }
            for (int j = 1; j < _job.Length; j++)
            {
                float v = MathUtil.Clamp(_job[j], -1f, 1f);
                if (Math.Abs(v) >= 0.02f) VoteJob(s, civ, (JobType)j, v);
            }
        }
    }
}
