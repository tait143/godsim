using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Capital advisor: makes the capital feel like the seat of power.
    ///
    /// At the capital (settlement with <c>IsCapital</c>) it
    ///  1. guards the seat: votes a Town Center when the capital has none (after a disaster or a relocation);
    ///  2. scales Administration with the empire: desired points grow with the civ's settlement count and citizens
    ///     (expansionist civs plan ahead), counting queued/under-construction administration as pending capacity,
    ///     and pushes Administrator-type jobs when admin halls are understaffed;
    ///  3. supports wonders: a wonder in the capital's BuildQueue or under construction gets producer votes for every
    ///     missing cost resource (placement-aware, plus one level of input chain), storage when the stockpile cannot
    ///     even hold the cost, and Builder jobs once the cost is paid ("Gathering stone for the Sun Pyramid (62%)");
    ///  4. raises civic grandeur appropriate to the age: grandest temple tier, top learning, a citadel (Castle),
    ///     a hall of government and spectacle, scaled by personality (piety, curiosity, aggression...), damped while
    ///     the capital is in crisis (starvation, homelessness, unrest) and re-weighted at war.
    /// Provinces get small negative votes for capital-only grandeur (prestige concentrates in the capital first).
    /// A capital relocation (IsCapital changed) is detected through blackboard memory and the new capital pushes a
    /// Town Center and administration for <see cref="CapitalTuning.RelocationWindowTicks"/>; a civ without any
    /// capital treats its most populous settlement as the acting seat.
    /// Everything is data-driven from <see cref="BuildingDefinition"/> fields and gated with
    /// <c>db.IsBuildingAvailable</c>. Deterministic, allocation-free except reason strings on strong votes.
    /// </summary>
    public sealed class CapitalAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in memory keys and logs.</summary>
        public const string Id = "capital";

        public override string AdvisorId => Id;
        public override int TickInterval => 24;

        private const PlacementRule SoftRules = PlacementRule.Central | PlacementRule.Outskirts | PlacementRule.Isolated;

        private static readonly string[] s_resNames = BuildResourceNames();

        private CapitalCatalogIndex _index;
        private readonly int[] _existing = new int[Counts.Buildings];
        private readonly int[] _queued = new int[Counts.Buildings];
        private float _adminNominal;
        private float _adminPending;
        private JobType _staffJob;
        private float _staffGap;
        private bool _tcIncomplete;
        private Building _wonderSite;
        private BuildOrder _wonderOrder;
        private PlacementRule _features;
        private bool _featuresReady;

        /// <summary>The catalog index in use (built lazily from the context database).</summary>
        public CapitalCatalogIndex Index => _index;

        public override void Initialize(SimContext ctx)
        {
            if (ctx != null && ctx.Db != null) EnsureIndex(ctx.Db);
        }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null) return;
            EnsureIndex(db);
            var race = RaceOf(db, civ.Race);
            Survey(ctx.State, db, race, s);
            var capital = FindCapital(ctx.State, civ);
            if (s.IsCapital && capital == s) AdviseCapital(ctx, civ, s, race);
            else AdviseProvince(ctx, civ, s, capital);
        }

        // ================================================================== capital
        private void AdviseCapital(SimContext ctx, Civilization civ, Settlement s, RaceDefinition race)
        {
            float freshness = TrackSeat(ctx, civ, s);
            bool crisis = InCrisis(ctx.Tick, s);
            bool atWar = AtWar(ctx.State, civ.Id);

            float urgency = AdviseSeat(ctx, civ, s, freshness, false);
            urgency = Math.Max(urgency, AdviseAdministration(ctx, civ, s));
            urgency = Math.Max(urgency, AdviseWonder(ctx, civ, s, crisis, atWar));
            urgency = Math.Max(urgency, AdviseGrandeur(ctx, civ, s, race, crisis, atWar));
            Urgency(s, urgency);
        }

        /// <summary>Records the seat; detects a relocation. Returns relocation freshness 1..0 (0 = none/expired).</summary>
        private float TrackSeat(SimContext ctx, Civilization civ, Settlement s)
        {
            long tick = ctx.Tick;
            bool hadLast = civ.Memory.TryGetValue(CapitalKeys.LastCapital, out float lastF);
            int last = hadLast ? (int)MathUtil.Sanitize(lastF, -1f) : -1;
            if (last != s.Id)
            {
                if (hadLast && last >= 0)
                {
                    s.Memory[CapitalKeys.SeatSince] = tick;
                    civ.Memory[CapitalKeys.Relocations] = MathUtil.Sanitize(civ.Mem(CapitalKeys.Relocations)) + 1f;
                    var old = ctx.State.Settlements.Get(last);
                    s.LogAi("[" + Id + "] The seat of power moved to " + s.Name + (old != null ? " from " + old.Name : "") + ": raising the instruments of rule");
                }
                civ.Memory[CapitalKeys.LastCapital] = s.Id;
            }
            s.Memory[CapitalKeys.WasCapital] = 1f;
            if (!s.Memory.TryGetValue(CapitalKeys.SeatSince, out float since)) return 0f;
            float elapsed = Math.Max(0f, tick - MathUtil.Sanitize(since, tick));
            return MathUtil.Clamp01(1f - elapsed / CapitalTuning.RelocationWindowTicks);
        }

        /// <summary>Town Center guard + relocation / interregnum administration. Returns urgency.</summary>
        private float AdviseSeat(SimContext ctx, Civilization civ, Settlement s, float freshness, bool interregnum)
        {
            var db = ctx.Db;
            float u = 0f;
            bool reseat = interregnum || freshness > 0f;
            int tc = (int)BuildingType.TownCenter;
            if (db.IsBuildingAvailable(civ, BuildingType.TownCenter))
            {
                if (_existing[tc] + _queued[tc] == 0)
                {
                    float st = reseat ? 1f : 0.9f;
                    string why = !IsStrong(civ, st) ? null
                        : interregnum ? "No seat of power: " + s.Name + " must rule the realm"
                        : reseat ? "Seat of power moved to " + s.Name + ": raising a town center"
                        : "The capital lost its town center";
                    VoteBuild(s, civ, BuildingType.TownCenter, st, why);
                    VoteJob(s, civ, JobType.Builder, 0.5f);
                    u = 1f;
                }
                else if (_tcIncomplete)
                {
                    VoteJob(s, civ, JobType.Builder, 0.6f);
                    u = 0.6f;
                }
            }
            if (reseat)
            {
                float f = interregnum ? 1f : freshness;
                int pick = PickAdmin(civ, s, 1, -1);
                if (pick >= 0)
                {
                    var def = db.Building((BuildingType)pick);
                    float st = 0.55f + 0.4f * f;
                    VoteBuild(s, civ, def.Type, st, IsStrong(civ, st) ? (interregnum ? "Acting seat " : "New capital ") + s.Name + " must govern the realm" : null);
                    if (def.HasJobs) VoteJob(s, civ, def.Job, 0.4f * f);
                }
                VoteJob(s, civ, JobType.Builder, 0.3f + 0.3f * f);
                u = Math.Max(u, 0.5f + 0.4f * f);
            }
            return u;
        }

        /// <summary>Administration coverage scaled with the empire's size. Returns urgency.</summary>
        private float AdviseAdministration(SimContext ctx, Civilization civ, Settlement s)
        {
            var state = ctx.State;
            int n = 0;
            float citizens = 0f;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var o = state.Settlements.Get(ids[i]);
                if (o == null || !o.IsAlive || o.CivilizationId != civ.Id) continue;
                n++;
                citizens += o.Population;
            }
            if (n < 1) n = 1;
            float expansion = AdvisorBoard.Trait(civ, ctx.Db, AITrait.Expansionism);
            float projected = n + (n >= 2 ? expansion * CapitalTuning.AdminProjection : 0f);
            float desired = CapitalTuning.AdminBase + CapitalTuning.AdminPerSettlement * (projected - 1f) + citizens * CapitalTuning.AdminPerCitizen;
            float served = MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Administration]);
            if (served <= 0f) served = _adminNominal;
            float have = served + CapitalTuning.PendingAdminShare * _adminPending;
            float gapRatio = desired > 0f ? MathUtil.Clamp01((desired - have) / desired) : 0f;

            if (_staffJob != JobType.None && _staffGap > 0f)
                VoteJob(s, civ, _staffJob, (0.25f + 0.5f * gapRatio) * _staffGap);

            if (gapRatio < CapitalTuning.AdminGapThreshold) return gapRatio * 0.5f;

            float st = MathUtil.Lerp(0.35f, 1f, gapRatio) * (0.85f + 0.3f * expansion);
            int cap = 1 + (n - 1) / 4;
            int pick = PickAdmin(civ, s, cap, -1);
            if (pick >= 0)
            {
                VoteBuild(s, civ, (BuildingType)pick, st, IsStrong(civ, st)
                    ? "Empire of " + n + " settlements needs administration (" + MathUtil.RoundToInt(have) + "/" + MathUtil.RoundToInt(desired) + ")" : null);
                if (gapRatio > 0.5f)
                {
                    int pick2 = PickAdmin(civ, s, cap, pick);
                    if (pick2 >= 0) VoteBuild(s, civ, (BuildingType)pick2, st * 0.5f, IsStrong(civ, st * 0.5f) ? "Administration still short for " + n + " settlements" : null);
                }
            }
            return gapRatio * 0.8f;
        }

        /// <summary>Wonder support: materials, storage and builders. Returns urgency.</summary>
        private float AdviseWonder(SimContext ctx, Civilization civ, Settlement s, bool crisis, bool atWar)
        {
            var db = ctx.Db;
            BuildingType type;
            if (_wonderSite != null) type = _wonderSite.Type;
            else if (_wonderOrder != null) type = _wonderOrder.Type;
            else { s.Memory.Remove(CapitalKeys.WonderProgress); return 0f; }
            if (!db.TryGetBuilding(type, out var def)) return 0f;
            string name = NameOf(def);
            float industry = (0.8f + 0.4f * AdvisorBoard.Trait(civ, db, AITrait.Industriousness))
                             * (crisis ? CapitalTuning.CrisisWonderDamp : 1f) * (atWar ? 0.85f : 1f);

            // Phase B: cost paid, raise it.
            if (_wonderSite != null && _wonderSite.CostPaid)
            {
                float prog = MathUtil.Clamp01(MathUtil.Sanitize(_wonderSite.ConstructionProgress));
                float st = MathUtil.Lerp(0.6f, 1f, 1f - prog) * industry;
                VoteJob(s, civ, JobType.Builder, st);
                if (IsStrong(civ, st)) s.LogAi("[" + Id + "] Builders to the " + name + " (" + Pct(prog) + "%)");
                s.Memory[CapitalKeys.WonderProgress] = 0.5f + 0.5f * prog;
                return 0.4f + 0.4f * (1f - prog);
            }

            // Phase A: gather the cost.
            var cost = def.Cost;
            var stock = s.Stockpile;
            float need = 0f, have = 0f;
            if (cost != null)
                for (int i = 0; i < cost.Length; i++)
                {
                    float amt = MathUtil.Sanitize(cost[i].Amount);
                    if (amt <= 0f) continue;
                    need += amt;
                    have += Math.Min(Math.Max(0f, stock.Get(cost[i].Type)), amt);
                }
            float progress = need > 0f ? MathUtil.Clamp01(have / need) : 1f;
            s.Memory[CapitalKeys.WonderProgress] = 0.5f * progress;

            bool storageVoted = false;
            if (cost != null)
                for (int i = 0; i < cost.Length; i++)
                {
                    var c = cost[i];
                    float amt = MathUtil.Sanitize(c.Amount);
                    if (amt <= 0f) continue;
                    float cur = Math.Max(0f, MathUtil.Sanitize(stock.Get(c.Type)));
                    float frac = MathUtil.Clamp01(cur / amt);
                    if (frac >= 0.999f) continue;
                    if (!storageVoted && stock.Capacity + 0.5f < amt)
                    {
                        storageVoted = true;
                        VoteStorage(ctx, civ, s, name, c.Type, amt, industry);
                    }
                    float share = need > 0f ? (amt - Math.Min(cur, amt)) / need : 0f;
                    float stalled = stock.Net(c.Type) > 0.01f ? 0f : 0.15f;
                    float st = Math.Min(1f, (0.4f + 0.3f * (1f - frac) + 0.3f * share + stalled) * industry);
                    int prod = PickProducer(ctx, civ, s, c.Type);
                    if (prod < 0) continue;
                    var pd = db.Building((BuildingType)prod);
                    VoteBuild(s, civ, pd.Type, st, IsStrong(civ, st) ? "Gathering " + ResName(c.Type) + " for the " + name + " (" + Pct(frac) + "%)" : null);
                    if (pd.HasJobs) VoteJob(s, civ, pd.Job, st * 0.8f);
                    FeedInputs(ctx, civ, s, pd, c.Type, st * 0.5f, name);
                }
            if (_wonderSite != null) VoteJob(s, civ, JobType.Builder, 0.25f * industry);
            return 0.45f + 0.45f * (1f - progress);
        }

        private void VoteStorage(SimContext ctx, Civilization civ, Settlement s, string wonder, ResourceType res, float amount, float industry)
        {
            var list = _index.Storage;
            for (int i = 0; i < list.Length; i++)
            {
                var def = ctx.Db.Building(list[i]);
                if (!CanAdd(ctx.Db, civ, s, def)) continue;
                float st = 0.85f * industry;
                VoteBuild(s, civ, def.Type, st, IsStrong(civ, st)
                    ? "Storage too small for " + MathUtil.RoundToInt(amount) + " " + ResName(res) + " (" + wonder + ")" : null);
                return;
            }
        }

        /// <summary>One level of the production chain: the producer's inputs, when stocks run thin.</summary>
        private void FeedInputs(SimContext ctx, Civilization civ, Settlement s, BuildingDefinition producer, ResourceType primary, float st, string wonder)
        {
            var inputs = producer.InputsPerWorker;
            if (inputs == null || st <= 0f) return;
            for (int i = 0; i < inputs.Length; i++)
            {
                var r = inputs[i].Type;
                if (r == primary || s.Stockpile.Get(r) >= CapitalTuning.InputBuffer) continue;
                int p2 = PickProducer(ctx, civ, s, r);
                if (p2 < 0 || p2 == (int)producer.Type) continue;
                var d2 = ctx.Db.Building((BuildingType)p2);
                VoteBuild(s, civ, d2.Type, st, IsStrong(civ, st) ? "Feeding the " + NameOf(producer) + " with " + ResName(r) + " for the " + wonder : null);
                if (d2.HasJobs) VoteJob(s, civ, d2.Job, st * 0.8f);
            }
        }

        /// <summary>Age-appropriate civic grandeur at the capital. Returns urgency.</summary>
        private float AdviseGrandeur(SimContext ctx, Civilization civ, Settlement s, RaceDefinition race, bool crisis, bool atWar)
        {
            var db = ctx.Db;
            float g = (crisis ? CapitalTuning.CrisisGrandeurDamp : 1f) * (s.Tier >= SettlementTier.Village ? 1f : 0.6f);
            float u = 0f;

            int faith = PickTop(db, _index.Faith, civ, s, -1);
            if (faith >= 0)
            {
                float st = CapitalTuning.FaithGrandeur * (0.6f + 0.8f * AdvisorBoard.Trait(civ, db, AITrait.Piety)) * g * (atWar ? 0.8f : 1f) + Favored(race, faith);
                VoteBuild(s, civ, (BuildingType)faith, st, IsStrong(civ, st) ? "The capital deserves the grandest temple of its age" : null);
                u = 0.3f;
            }

            float curiosity = 0.6f + 0.8f * AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            int learn = PickTop(db, _index.Learning, civ, s, -1);
            if (learn >= 0)
            {
                float st = CapitalTuning.LearningGrandeur * curiosity * g + Favored(race, learn);
                VoteBuild(s, civ, (BuildingType)learn, st, IsStrong(civ, st) ? "A seat of learning for the realm" : null);
                int learn2 = PickTop(db, _index.Learning, civ, s, learn);
                if (learn2 >= 0) VoteBuild(s, civ, (BuildingType)learn2, st * 0.6f, IsStrong(civ, st * 0.6f) ? "Scholars gather at the capital" : null);
                u = Math.Max(u, 0.25f);
            }

            int citadel = PickTop(db, _index.Citadel, civ, s, -1);
            if (citadel >= 0)
            {
                float war = atWar ? 1.5f : 1f;
                float st = CapitalTuning.CitadelGrandeur * (0.6f + 0.8f * AdvisorBoard.Trait(civ, db, AITrait.Aggression)) * war
                           * (s.Tier >= SettlementTier.Village ? 1f : 0.6f) + Favored(race, citadel);
                VoteBuild(s, civ, (BuildingType)citadel, st, IsStrong(civ, st) ? (atWar ? "War: fortify the seat of power" : "A fortified seat of power") : null);
                u = Math.Max(u, atWar ? 0.6f : 0.3f);
            }

            int hall = PickTop(db, _index.Admin, civ, s, -1);
            if (hall >= 0)
            {
                float st = CapitalTuning.AdminGrandeur * g + Favored(race, hall);
                VoteBuild(s, civ, (BuildingType)hall, st, IsStrong(civ, st) ? "A hall of government befitting the capital" : null);
            }

            int culture = PickTop(db, _index.Culture, civ, s, -1);
            if (culture >= 0)
            {
                float st = CapitalTuning.CultureGrandeur * (0.7f + 0.6f * AdvisorBoard.Trait(civ, db, AITrait.Mercantilism)) * g * (atWar ? 0.5f : 1f) + Favored(race, culture);
                VoteBuild(s, civ, (BuildingType)culture, st, IsStrong(civ, st) ? "Spectacle for the glory of the capital" : null);
            }
            return u;
        }

        // ================================================================== provinces
        private void AdviseProvince(SimContext ctx, Civilization civ, Settlement s, Settlement capital)
        {
            var db = ctx.Db;
            if (s.Mem(CapitalKeys.WasCapital) > 0.5f)
            {
                s.Memory[CapitalKeys.WasCapital] = 0f;
                s.Memory[CapitalKeys.LostTick] = ctx.Tick;
                s.LogAi("[" + Id + "] " + s.Name + " is no longer the seat of power");
            }

            if (capital == null)
            {
                if (IsActingSeat(ctx.State, civ, s))
                {
                    float u = AdviseSeat(ctx, civ, s, 1f, true);
                    Urgency(s, Math.Max(0.9f, u));
                }
                else Urgency(s, 0f);
                return;
            }

            Urgency(s, 0f);
            if (s.Tier >= SettlementTier.City) return; // a great city may host grandeur of its own
            if (s.Memory.TryGetValue(CapitalKeys.LostTick, out float lost) && ctx.Tick - MathUtil.Sanitize(lost) < CapitalTuning.RelocationWindowTicks) return; // former seat keeps its legacy

            var wonders = _index.Wonders;
            for (int i = 0; i < wonders.Length; i++)
            {
                var t = wonders[i];
                if (!db.IsBuildingAvailable(civ, t) || Total((int)t) > 0) continue; // respect a project already under way here
                VoteBuild(s, civ, t, -CapitalTuning.ProvinceWonderPenalty);
            }
            PenalizeGrand(ctx, civ, s, capital, _index.Faith, CapitalTuning.ProvinceFaithPenalty);
            PenalizeGrand(ctx, civ, s, capital, _index.Learning, CapitalTuning.ProvinceLearningPenalty);
            PenalizeGrand(ctx, civ, s, capital, _index.Admin, CapitalTuning.ProvinceAdminPenalty);
        }

        private void PenalizeGrand(SimContext ctx, Civilization civ, Settlement s, Settlement capital, BuildingType[] list, float penalty)
        {
            var db = ctx.Db;
            for (int i = 0; i < list.Length; i++)
            {
                var t = list[i];
                if (!db.IsBuildingAvailable(civ, t)) continue;
                var def = db.Building(t);
                if (def.Category == BuildingCategory.Military) continue; // defense is never vanity
                if (def.MinAge < CapitalTuning.GrandMinAge) return;   // the top tier is not grandeur yet
                if (Total((int)t) > 0 || SettlementHas(ctx.State, capital, t)) return;
                VoteBuild(s, civ, t, -penalty);
                return;
            }
        }

        // ================================================================== survey & picks
        private void Survey(WorldState state, GameDatabase db, RaceDefinition race, Settlement s)
        {
            Array.Clear(_existing, 0, _existing.Length);
            Array.Clear(_queued, 0, _queued.Length);
            _adminNominal = 0f;
            _adminPending = 0f;
            _staffJob = JobType.None;
            _staffGap = 0f;
            _tcIncomplete = false;
            _wonderSite = null;
            _wonderOrder = null;
            _featuresReady = false;

            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= _existing.Length) continue;
                _existing[t]++;
                float adm = _index.AdminValue[t];
                if (b.IsComplete)
                {
                    _adminNominal += adm;
                    if (adm > 0f && db.TryGetBuilding(b.Type, out var d) && d.HasJobs && b.Workers.Count < d.JobSlots)
                    {
                        float gap = 1f - b.Workers.Count / (float)d.JobSlots;
                        if (gap > _staffGap) { _staffGap = gap; _staffJob = d.Job; }
                    }
                }
                else
                {
                    _adminPending += adm;
                    if (b.Type == BuildingType.TownCenter) _tcIncomplete = true;
                    if (_wonderSite == null && IsWonderFor(t, race)) _wonderSite = b;
                }
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= _queued.Length) continue;
                _queued[t]++;
                _adminPending += _index.AdminValue[t];
                if (IsWonderFor(t, race) && (_wonderOrder == null || o.Priority > _wonderOrder.Priority)) _wonderOrder = o;
            }
        }

        private int Total(int t) => _existing[t] + _queued[t];

        private bool CanAdd(GameDatabase db, Civilization civ, Settlement s, BuildingDefinition def)
        {
            if (def == null || !db.IsBuildingAvailable(civ, def.Type)) return false;
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            int total = Total((int)def.Type);
            if (def.MaxPerSettlement >= 0 && total >= def.MaxPerSettlement) return false;
            if (def.UniquePerCivilization && total > 0) return false;
            return true;
        }

        /// <summary>Best administration building that can still be added (unlimited types capped at <paramref name="unlimitedCap"/>).</summary>
        private int PickAdmin(Civilization civ, Settlement s, int unlimitedCap, int exclude)
        {
            var db = _index.Db;
            var list = _index.Admin;
            for (int i = 0; i < list.Length; i++)
            {
                int t = (int)list[i];
                if (t == exclude) continue;
                var def = db.Building(list[i]);
                if (!CanAdd(db, civ, s, def)) continue;
                if (def.MaxPerSettlement < 0 && Total(t) >= unlimitedCap) continue;
                return t;
            }
            return -1;
        }

        /// <summary>The grandest available tier the settlement lacks; -1 when it already owns a tier at least that grand.</summary>
        private int PickTop(GameDatabase db, BuildingType[] list, Civilization civ, Settlement s, int exclude)
        {
            for (int i = 0; i < list.Length; i++)
            {
                int t = (int)list[i];
                if (t == exclude || !db.IsBuildingAvailable(civ, list[i])) continue;
                if (Total(t) > 0) return -1;
                if (!CanAdd(db, civ, s, db.Building(list[i]))) continue;
                return t;
            }
            return -1;
        }

        /// <summary>Best producer of a resource that is available, fits the settlement and its terrain.</summary>
        private int PickProducer(SimContext ctx, Civilization civ, Settlement s, ResourceType r)
        {
            var list = _index.Producers[(int)r];
            for (int i = 0; i < list.Length; i++)
            {
                var def = ctx.Db.Building(list[i]);
                if (!CanAdd(ctx.Db, civ, s, def)) continue;
                if (!PlacementFeasible(ctx.State.Map, s, def.Placement)) continue;
                return (int)def.Type;
            }
            return -1;
        }

        private bool PlacementFeasible(WorldMap map, Settlement s, PlacementRule rule)
        {
            var hard = rule & ~SoftRules;
            if (hard == PlacementRule.None) return true;
            if (!_featuresReady) { _features = ScanFeatures(map, s); _featuresReady = true; }
            return (hard & ~_features) == PlacementRule.None;
        }

        /// <summary>Terrain features within reach of the settlement, as satisfied placement rules (allocation-free).</summary>
        private static PlacementRule ScanFeatures(WorldMap map, Settlement s)
        {
            var f = SoftRules;
            if (map == null) return f;
            int r = MathUtil.Clamp(s.TerritoryRadius + 2, 3, 24);
            for (int y = s.Center.Y - r; y <= s.Center.Y + r; y++)
                for (int x = s.Center.X - r; x <= s.Center.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    var b = map.Biomes[i];
                    var fl = map.Flags[i];
                    if (b.IsWater() || (fl & TileFlags.River) != 0) f |= PlacementRule.NearWater;
                    if ((fl & TileFlags.River) != 0) f |= PlacementRule.NearRiver;
                    if ((fl & TileFlags.Coastal) != 0) f |= PlacementRule.Coastal;
                    if (b == Biome.Hills) f |= PlacementRule.OnHills;
                    if (b == Biome.Mountain) f |= PlacementRule.NearMountain;
                    if ((fl & TileFlags.Trees) != 0 || b == Biome.Forest || b == Biome.Jungle || b == Biome.Taiga) f |= PlacementRule.NearForest;
                    if ((fl & TileFlags.OreDeposit) != 0) f |= PlacementRule.NearOre;
                    if ((fl & TileFlags.StoneDeposit) != 0) f |= PlacementRule.NearStone;
                    if ((fl & TileFlags.GoldDeposit) != 0) f |= PlacementRule.NearGold;
                    if ((fl & TileFlags.CoalDeposit) != 0) f |= PlacementRule.NearCoal;
                    if ((fl & TileFlags.OilDeposit) != 0) f |= PlacementRule.NearOil;
                    if (b.IsLand() && ((fl & TileFlags.FertileSoil) != 0 || map.Fertility[i] >= 0.55f)) f |= PlacementRule.Fertile;
                }
            return f;
        }

        // ================================================================== helpers
        private void EnsureIndex(GameDatabase db)
        {
            if (_index == null || !ReferenceEquals(_index.Db, db)) _index = new CapitalCatalogIndex(db);
        }

        private bool IsWonderFor(int t, RaceDefinition race)
        {
            if (_index.IsWonderType[t]) return true;
            if (race == null || race.UniqueBuildings == null) return false;
            for (int i = 0; i < race.UniqueBuildings.Length; i++) if ((int)race.UniqueBuildings[i] == t) return true;
            return false;
        }

        private static float Favored(RaceDefinition race, int t)
        {
            if (race == null || race.FavoredBuildings == null) return 0f;
            for (int i = 0; i < race.FavoredBuildings.Length; i++) if ((int)race.FavoredBuildings[i] == t) return CapitalTuning.FavoredBonus;
            return 0f;
        }

        /// <summary>The civ's valid capital: CapitalId if consistent, else any own settlement flagged IsCapital; null if none.</summary>
        private static Settlement FindCapital(WorldState state, Civilization civ)
        {
            var cap = state.Settlements.Get(civ.CapitalId);
            if (cap != null && cap.IsAlive && cap.IsCapital && cap.CivilizationId == civ.Id) return cap;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var o = state.Settlements.Get(ids[i]);
                if (o != null && o.IsAlive && o.IsCapital && o.CivilizationId == civ.Id) return o;
            }
            return null;
        }

        /// <summary>Without any capital, the most populous settlement (tie: lowest id) acts as the seat.</summary>
        private static bool IsActingSeat(WorldState state, Civilization civ, Settlement s)
        {
            Settlement best = null;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var o = state.Settlements.Get(ids[i]);
                if (o == null || !o.IsAlive || o.CivilizationId != civ.Id) continue;
                if (best == null || o.Population > best.Population || (o.Population == best.Population && o.Id < best.Id)) best = o;
            }
            return best == s;
        }

        private static bool SettlementHas(WorldState state, Settlement s, BuildingType t)
        {
            if (s == null) return false;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b != null && !b.IsDestroyed && b.Type == t) return true;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++) if (q[i] != null && q[i].Type == t) return true;
            return false;
        }

        private static bool InCrisis(long tick, Settlement s)
        {
            if (s.LastStarvationTick >= 0 && tick - s.LastStarvationTick < SimTime.TicksPerYear) return true;
            if (s.Homeless > Math.Max(3, s.Population / 4)) return true;
            return MathUtil.Sanitize(s.Unrest) > 0.6f;
        }

        private static bool AtWar(WorldState state, int civId)
        {
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w != null && w.IsActive && (w.AttackerId == civId || w.DefenderId == civId)) return true;
            }
            return false;
        }

        private static RaceDefinition RaceOf(GameDatabase db, RaceId race)
        {
            int i = (int)race;
            return db != null && i >= 0 && i < db.Races.Count ? db.Races[i] : null;
        }

        private static bool IsStrong(Civilization civ, float strength) =>
            Math.Abs(MathUtil.Clamp(MathUtil.Sanitize(strength), -AdvisorBoard.MaxContribution, AdvisorBoard.MaxContribution) * AdvisorBoard.Weight(civ, Id)) >= 0.5f;

        private static string NameOf(BuildingDefinition def) => string.IsNullOrEmpty(def.Name) ? def.Type.ToString() : def.Name;

        private static string ResName(ResourceType r)
        {
            int i = (int)r;
            return i >= 0 && i < s_resNames.Length ? s_resNames[i] : "goods";
        }

        private static string Pct(float f) => MathUtil.RoundToInt(MathUtil.Clamp01(f) * 100f).ToString();

        private static string[] BuildResourceNames()
        {
            var all = EnumUtil.AllResources;
            var a = new string[Counts.Resources];
            for (int i = 0; i < all.Length; i++) a[(int)all[i]] = all[i].ToString().ToLowerInvariant();
            return a;
        }
    }
}
