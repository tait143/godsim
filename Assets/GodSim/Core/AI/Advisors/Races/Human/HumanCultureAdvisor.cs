using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Race advisor that gives Human settlements their cultural soul. It never queues anything itself: it votes on
    /// the Advisor Council blackboard (<see cref="AdvisorBoard"/>), and the settlement governor folds those votes
    /// into its utility scores (BuildBias, ±3) and job priorities (JobBias). Only settlements of Human
    /// civilizations are touched.
    ///
    /// <para><b>Culture notes (Humans).</b> Humans are adaptable, ambitious traders and builders. Their towns are
    /// orderly grids laid out around a <i>market square</i>: as soon as a hamlet becomes a Village the first great
    /// project is the Market, and merchants are prized staff. They are plains farmers at heart, so they abandon
    /// foraging as soon as the plough arrives (Farms over Gatherer Huts) and raise a Mill in the Medieval age.
    /// Commerce is their road to greatness: Guildhalls and Banks follow the Market, and each new age's trade
    /// buildings are adopted eagerly. Faith is pragmatic: a cathedral tradition climbs from shrine to temple to
    /// Cathedral and peaks in the Grand Cathedral, for which the capital stockpiles stone, wood, gold, tools and
    /// goods the moment it becomes available (unless the strategy already queued it). Castles are status symbols of
    /// the Medieval age, and Humans rush into each age's new industry (Industrial factories above all).
    /// They react like people: after a war they build walls and barracks resolutely; in a famine they plough more
    /// fields and crowd the temples; they are pious when the god blesses them and defiant when it punishes them.
    /// Expansion is carried by commerce rather than conquest, so soldiers are only favoured while war is near.</para>
    ///
    /// <para>Determinism and cost: no randomness, iteration by index only, no allocations per run (scratch arrays
    /// live in fields, reason strings are constants or built once in the static constructor). Runs every 24 ticks.</para>
    /// </summary>
    public sealed class HumanCultureAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in blackboard keys ("ai.w.race_human", "ai.adv.race_human.u").</summary>
        public const string Id = "race_human";

        // ------------------------------------------------------------------ blackboard keys read (other modules)
        /// <summary>Settlement, written by economy: days of food left at current consumption.</summary>
        public const string FoodDaysKey = "econ.foodDays";
        /// <summary>Civilization, written by strategy: number of active wars.</summary>
        public const string ActiveWarsKey = "ai.wars";

        // ------------------------------------------------------------------ tuning (vote strengths, before learned weight)
        private const float MarketFirst = 1.0f;
        private const float FarmBase = 0.6f;
        private const float GathererSuppress = -0.6f;
        private const float HunterSuppress = -0.3f;
        private const float MillVote = 0.7f;
        private const float GuildVote = 0.75f;
        private const float BankVote = 0.75f;
        private const float AgeCommerceVote = 0.45f;
        private const float FavoredVote = 0.25f;
        private const float EarlyFaithVote = 0.35f;
        private const float CathedralVote = 0.9f;
        private const float CastleVote = 0.6f;
        private const float AgeIndustryVote = 0.55f;
        private const float FactoryVote = 0.9f;
        private const float StockpileVote = 0.7f;
        private const float WallVote = 0.85f;
        private const float BarracksVote = 0.75f;

        private const float FamineFoodDays = 30f;
        private const int StarvationMemoryTicks = SimTime.TicksPerYear;
        private const int PostWarWindowTicks = SimTime.TicksPerYear * 4;

        private const int None = -1;

        private static readonly string[] s_stockpileReasons = BuildStockpileReasons();

        private readonly int[] _have = new int[Counts.Buildings];

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 24;

        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (civ == null || civ.Race != RaceId.Human || s == null || ctx == null || ctx.Db == null) return;
            var db = ctx.Db;
            var state = ctx.State;
            CountBuildings(state, s);

            float famine = FamineSeverity(ctx, s);
            float resolve = WarResolve(state, civ, ctx.Tick);
            float blessed = MathUtil.Clamp01(Math.Max(MathUtil.Sanitize(civ.GodFavor), AdvisorBoard.GodGratitude(civ)));
            float punished = MathUtil.Clamp01(Math.Max(-MathUtil.Sanitize(civ.GodFavor), AdvisorBoard.GodFear(civ)));
            bool isCapital = s.IsCapital || civ.CapitalId == s.Id;
            float urgency = 0.15f;

            // 1) The market square: the civic heart of every human town, first in line from Village tier on.
            bool hasMarket = Has(BuildingType.Market);
            if (!hasMarket && s.Tier >= SettlementTier.Village && CanAdd(db, civ, s, BuildingType.Market))
            {
                VoteBuild(s, civ, BuildingType.Market, MarketFirst, "the market square comes first: every human town grows around its stalls");
                urgency = Math.Max(urgency, 0.6f);
            }

            // 2) Plains farmers: the plough replaces foraging as soon as it is known; the Mill crowns the fields.
            int farm = BestOfJob(db, civ, s, BuildingCategory.Food, JobType.Farmer);
            if (farm != None)
            {
                int wantedFarms = 1 + s.Population / 20;
                if (_have[farm] < wantedFarms || famine > 0f)
                    VoteBuild(s, civ, (BuildingType)farm, FarmBase + 0.4f * famine, "orderly fields on the plains: humans farm, they do not forage");
                SuppressForaging(db, civ, s);
                VoteJob(s, civ, JobType.Gatherer, -0.3f);
            }
            if (!Has(BuildingType.Mill) && CountFarmland(db) > 0 && CanAdd(db, civ, s, BuildingType.Mill))
                VoteBuild(s, civ, BuildingType.Mill, MillVote, "a miller's wheel to turn the plains' grain into bread");

            // 3) Commerce and guild ambition.
            if (hasMarket || Queued(s, BuildingType.Market))
            {
                if (!Has(BuildingType.Guildhall) && CanAdd(db, civ, s, BuildingType.Guildhall))
                    VoteBuild(s, civ, BuildingType.Guildhall, GuildVote, "the guilds organise craft and coin around the market");
                if (!Has(BuildingType.Bank) && CanAdd(db, civ, s, BuildingType.Bank))
                    VoteBuild(s, civ, BuildingType.Bank, BankVote, "counting-houses for the merchant families");
            }
            VoteNewestOfCategory(db, civ, s, BuildingCategory.Commerce, AgeCommerceVote, "eager traders embrace the new age's commerce");
            VoteFavored(db, civ, s);

            // 4) Faith: pragmatic. Pious when blessed, defiant when punished; a cathedral tradition from the Medieval age.
            float faithMul = MathUtil.Clamp(1f + 0.6f * blessed - 0.9f * punished + 0.5f * famine, 0.1f, 1.6f);
            int faith = BestOfCategory(db, civ, s, BuildingCategory.Religion);
            if (faith != None && faith != (int)BuildingType.Cathedral && !Has((BuildingType)faith) && s.Tier >= SettlementTier.Hamlet)
                VoteBuild(s, civ, (BuildingType)faith, EarlyFaithVote * faithMul, "a proper parish for a proper town");
            if (!Has(BuildingType.Cathedral) && s.Tier >= SettlementTier.Town && CanAdd(db, civ, s, BuildingType.Cathedral))
            {
                VoteBuild(s, civ, BuildingType.Cathedral, (isCapital ? 1f : CathedralVote) * Math.Max(0.5f, faithMul), "raise a cathedral to the heavens; the guilds will pay for the glass");
                urgency = Math.Max(urgency, 0.5f);
            }
            if (blessed > 0.4f)
            {
                if (faith != None) VoteBuild(s, civ, (BuildingType)faith, 0.5f * blessed, "the heavens smile on us: give thanks at the altar");
                VoteJob(s, civ, JobType.Priest, 0.4f * blessed);
            }
            else if (punished > 0.4f)
            {
                if (faith != None) VoteBuild(s, civ, (BuildingType)faith, -0.5f * punished, "the god smites us; we will not grovel at its altars");
                VoteJob(s, civ, JobType.Priest, -0.3f * punished);
                int defiantWall = BestWall(db, civ, s);
                if (defiantWall != None) VoteBuild(s, civ, (BuildingType)defiantWall, 0.4f * punished, "trust in stone, not in heaven");
            }

            // 5) The Grand Cathedral: the capital starts stockpiling materials as soon as it becomes available.
            if (isCapital && db.IsBuildingAvailable(civ, BuildingType.GrandCathedral) && !CivHasOrQueued(state, civ, BuildingType.GrandCathedral))
                urgency = Math.Max(urgency, StockpileForGrandCathedral(db, civ, s));

            // 6) Castles as status in the Medieval age.
            if (civ.Age >= AgeId.Medieval && !Has(BuildingType.Castle) && (isCapital || s.Tier >= SettlementTier.Town) && CanAdd(db, civ, s, BuildingType.Castle))
                VoteBuild(s, civ, BuildingType.Castle, CastleVote + 0.3f * resolve, "a lord's castle crowns every proper human town");

            // 7) Ambitious early adoption of each age's industry.
            VoteNewestOfCategory(db, civ, s, BuildingCategory.Industry, AgeIndustryVote, "first to adopt the new age's industry");
            if (!Has(BuildingType.Factory) && CanAdd(db, civ, s, BuildingType.Factory))
                VoteBuild(s, civ, BuildingType.Factory, FactoryVote, "smokestacks of progress: humans industrialise early");

            // 8) After war (or during one): walls and barracks, resolutely.
            if (resolve > 0f)
            {
                int wall = BestWall(db, civ, s);
                if (wall != None) VoteBuild(s, civ, (BuildingType)wall, Math.Max(0.6f, WallVote * resolve), "never again: wall the town after the war");
                if (_have[(int)BuildingType.Barracks] < 1 && CanAdd(db, civ, s, BuildingType.Barracks))
                    VoteBuild(s, civ, BuildingType.Barracks, Math.Max(0.6f, BarracksVote * resolve), "drill the militia; the realm must be ready");
                VoteJob(s, civ, JobType.Soldier, 0.4f * resolve);
                urgency = Math.Max(urgency, resolve);
            }

            // 9) Famine: more fields, a granary, and faith rises.
            if (famine > 0f)
            {
                if (_have[(int)BuildingType.Granary] < 1 && CanAdd(db, civ, s, BuildingType.Granary))
                    VoteBuild(s, civ, BuildingType.Granary, 0.4f * famine, "store the grain against the next lean year");
                if (faith != None && punished <= 0.4f)
                    VoteBuild(s, civ, (BuildingType)faith, 0.6f * famine, "in lean years the faithful crowd the temples");
                VoteJob(s, civ, JobType.Farmer, 0.6f * famine);
                VoteJob(s, civ, JobType.Priest, 0.3f * famine);
                urgency = Math.Max(urgency, famine);
            }

            // 10) Job flavour: merchants, farmers and builders of orderly grid towns.
            VoteJob(s, civ, JobType.Merchant, hasMarket ? 0.6f : 0.35f);
            VoteJob(s, civ, JobType.Farmer, 0.3f);
            VoteJob(s, civ, JobType.Builder, s.BuildQueue.Count >= 3 ? 0.5f : 0.3f);

            Urgency(s, urgency);
        }

        // ================================================================== signals
        /// <summary>0..1: how hungry the settlement is (low food reserves or recent starvation).</summary>
        private static float FamineSeverity(SimContext ctx, Settlement s)
        {
            float sev = 0f;
            if (s.Memory.TryGetValue(FoodDaysKey, out float days))
            {
                days = MathUtil.Sanitize(days, FamineFoodDays);
                if (days < FamineFoodDays) sev = MathUtil.Clamp01((FamineFoodDays - days) / FamineFoodDays);
            }
            if (s.LastStarvationTick >= 0 && ctx.Tick - s.LastStarvationTick <= StarvationMemoryTicks) sev = Math.Max(sev, 0.8f);
            return sev;
        }

        /// <summary>0..1: resolve to fortify. Strongest right after a war ends, fading over four years; 0.5 during a war.</summary>
        private static float WarResolve(WorldState state, Civilization civ, long tick)
        {
            float resolve = MathUtil.Sanitize(civ.Mem(ActiveWarsKey)) > 0.5f ? 0.5f : 0f;
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || (w.AttackerId != civ.Id && w.DefenderId != civ.Id)) continue;
                if (w.IsActive) { resolve = Math.Max(resolve, 0.5f); continue; }
                if (w.EndTick < 0) continue;
                long since = tick - w.EndTick;
                if (since < 0 || since > PostWarWindowTicks) continue;
                resolve = Math.Max(resolve, 1f - 0.5f * since / (float)PostWarWindowTicks);
            }
            return MathUtil.Clamp01(resolve);
        }

        // ================================================================== Grand Cathedral
        /// <summary>Votes producers (and storage) for every cost resource the capital is short of. Returns urgency.</summary>
        private float StockpileForGrandCathedral(GameDatabase db, Civilization civ, Settlement s)
        {
            if (!db.TryGetBuilding(BuildingType.GrandCathedral, out var gc) || gc.Cost == null) return 0f;
            float worst = 0f, biggest = 0f;
            for (int i = 0; i < gc.Cost.Length; i++)
            {
                var c = gc.Cost[i];
                float need = MathUtil.Sanitize(c.Amount);
                if (need <= 0f) continue;
                if (need > biggest) biggest = need;
                float shortfall = MathUtil.Clamp01(1f - MathUtil.Sanitize(s.Stockpile.Get(c.Type)) / need);
                if (shortfall <= 0f) continue;
                if (shortfall > worst) worst = shortfall;
                int producer = BestProducer(db, civ, s, c.Type);
                if (producer == None) continue;
                VoteBuild(s, civ, (BuildingType)producer, Math.Max(0.3f, StockpileVote * shortfall), s_stockpileReasons[(int)c.Type]);
                if (db.TryGetBuilding((BuildingType)producer, out var pd)) VoteJob(s, civ, pd.Job, 0.3f * shortfall);
            }
            if (worst > 0f && s.Stockpile.Capacity < biggest && CanAdd(db, civ, s, BuildingType.Storehouse))
                VoteBuild(s, civ, BuildingType.Storehouse, 0.6f, "storerooms for the Grand Cathedral's stone and timber");
            if (worst > 0f) VoteJob(s, civ, JobType.Builder, 0.2f);
            return 0.3f + 0.4f * worst;
        }

        /// <summary>True when any settlement of the civ has the building (built or under construction) or has it queued.</summary>
        private static bool CivHasOrQueued(WorldState state, Civilization civ, BuildingType type)
        {
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var st = state.Settlements.Get(ids[i]);
                if (st == null || !st.IsAlive) continue;
                if (Queued(st, type)) return true;
                var b = st.BuildingIds;
                for (int j = 0; j < b.Count; j++)
                {
                    var bld = state.Buildings.Get(b[j]);
                    if (bld != null && !bld.IsDestroyed && bld.Type == type) return true;
                }
            }
            return false;
        }

        // ================================================================== data-driven helpers
        private void CountBuildings(WorldState state, Settlement s)
        {
            Array.Clear(_have, 0, _have.Length);
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t >= 0 && t < _have.Length) _have[t]++;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                if (q[i] == null) continue;
                int t = (int)q[i].Type;
                if (t >= 0 && t < _have.Length) _have[t]++;
            }
        }

        private bool Has(BuildingType t) => _have[(int)t] > 0;

        private static bool Queued(Settlement s, BuildingType t)
        {
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++) if (q[i] != null && q[i].Type == t) return true;
            return false;
        }

        /// <summary>Available to the civ (the single source of truth), tier reached and per-settlement cap not hit.</summary>
        private bool CanAdd(GameDatabase db, Civilization civ, Settlement s, BuildingType t)
        {
            if (!db.IsBuildingAvailable(civ, t) || !db.TryGetBuilding(t, out var d)) return false;
            if (s.Tier < d.MinTier) return false;
            return d.MaxPerSettlement < 0 || _have[(int)t] < d.MaxPerSettlement;
        }

        private static bool IsWonderLike(BuildingDefinition d) => d.IsWonder || d.UniquePerCivilization || d.Category == BuildingCategory.Wonder;

        /// <summary>Most advanced (highest MinAge, then lowest id) buildable non-wonder of a category; -1 if none.</summary>
        private int BestOfCategory(GameDatabase db, Civilization civ, Settlement s, BuildingCategory cat)
        {
            int best = None; AgeId bestAge = AgeId.Dawn;
            var defs = db.Buildings;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null || d.Category != cat || IsWonderLike(d) || !CanAdd(db, civ, s, d.Type)) continue;
                if (best == None || d.MinAge > bestAge) { best = i; bestAge = d.MinAge; }
            }
            return best;
        }

        /// <summary>Most advanced buildable non-wonder of a category employing a given job; -1 if none.</summary>
        private int BestOfJob(GameDatabase db, Civilization civ, Settlement s, BuildingCategory cat, JobType job)
        {
            int best = None; AgeId bestAge = AgeId.Dawn;
            var defs = db.Buildings;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null || d.Category != cat || d.Job != job || IsWonderLike(d) || !CanAdd(db, civ, s, d.Type)) continue;
                if (best == None || d.MinAge > bestAge) { best = i; bestAge = d.MinAge; }
            }
            return best;
        }

        /// <summary>Strongest buildable wall (Military category, wall shape); -1 if none.</summary>
        private int BestWall(GameDatabase db, Civilization civ, Settlement s)
        {
            int best = None; AgeId bestAge = AgeId.Dawn;
            var defs = db.Buildings;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null || d.Category != BuildingCategory.Military || d.Shape != BuildingShape.Wall || IsWonderLike(d) || !CanAdd(db, civ, s, d.Type)) continue;
                if (best == None || d.MinAge > bestAge) { best = i; bestAge = d.MinAge; }
            }
            return best;
        }

        /// <summary>Best buildable producer of a resource: output per worker x slots, penalised by input count; -1 if none.</summary>
        private int BestProducer(GameDatabase db, Civilization civ, Settlement s, ResourceType res)
        {
            int best = None; float bestScore = 0f;
            var defs = db.Buildings;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null || IsWonderLike(d) || d.OutputsPerWorker == null) continue;
                float amount = 0f;
                for (int k = 0; k < d.OutputsPerWorker.Length; k++)
                    if (d.OutputsPerWorker[k].Type == res) amount += MathUtil.Sanitize(d.OutputsPerWorker[k].Amount);
                if (amount <= 0f || !CanAdd(db, civ, s, d.Type)) continue;
                int inputs = d.InputsPerWorker == null ? 0 : d.InputsPerWorker.Length;
                float score = amount * Math.Max(1, d.JobSlots) / (1f + inputs);
                if (score > bestScore) { best = i; bestScore = score; }
            }
            return best;
        }

        /// <summary>Number of completed/planned farmland buildings (Food category, Farmer job) in the settlement.</summary>
        private int CountFarmland(GameDatabase db)
        {
            int n = 0;
            var defs = db.Buildings;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d != null && d.Category == BuildingCategory.Food && d.Job == JobType.Farmer) n += _have[i];
            }
            return n;
        }

        /// <summary>Once the plough is known, foraging buildings (Gatherer and, less so, Hunter food buildings) are shunned.</summary>
        private void SuppressForaging(GameDatabase db, Civilization civ, Settlement s)
        {
            var defs = db.Buildings;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null || d.Category != BuildingCategory.Food || IsWonderLike(d) || !db.IsBuildingAvailable(civ, d.Type)) continue;
                if (d.Job == JobType.Gatherer) VoteBuild(s, civ, d.Type, GathererSuppress, "foraging is for the desperate; plough the plains instead");
                else if (d.Job == JobType.Hunter) VoteBuild(s, civ, d.Type, HunterSuppress, null);
            }
        }

        /// <summary>Votes for buildings of a category introduced in the civ's current age (non-wonders, not yet present).</summary>
        private void VoteNewestOfCategory(GameDatabase db, Civilization civ, Settlement s, BuildingCategory cat, float strength, string reason)
        {
            var defs = db.Buildings;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null || d.Category != cat || d.MinAge != civ.Age || IsWonderLike(d) || _have[i] > 0) continue;
                if (!CanAdd(db, civ, s, d.Type)) continue;
                VoteBuild(s, civ, d.Type, strength, reason);
            }
        }

        /// <summary>A gentle, standing preference for the race's favoured buildings (from the race definition).</summary>
        private void VoteFavored(GameDatabase db, Civilization civ, Settlement s)
        {
            int r = (int)civ.Race;
            if (r < 0 || r >= db.Races.Count) return;
            var race = db.Races[r];
            if (race == null || race.FavoredBuildings == null) return;
            var fav = race.FavoredBuildings;
            for (int i = 0; i < fav.Length; i++)
            {
                var t = fav[i];
                if ((int)t < 0 || (int)t >= _have.Length || _have[(int)t] > 0 || !CanAdd(db, civ, s, t)) continue;
                VoteBuild(s, civ, t, FavoredVote, null);
            }
        }

        private static string[] BuildStockpileReasons()
        {
            var a = new string[Counts.Resources];
            for (int i = 0; i < a.Length; i++) a[i] = "the capital stockpiles " + ((ResourceType)i).ToString().ToLowerInvariant() + " for the Grand Cathedral";
            return a;
        }
    }
}
