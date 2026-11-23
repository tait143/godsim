using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Founds new settlements with settlers. Every <see cref="Interval"/> ticks, for each alive civilization
    /// below <c>SimConfig.MaxSettlementsPerCiv</c> and off cooldown (<c>ai.nextExpandTick</c>):
    /// <list type="number">
    /// <item><b>Timing.</b> During a war or a famine (any town under 20 food days, or a recent FamineEvent) expansion is on
    /// hold, except for Orcs and Expansionism-driven civs (dynamic trait &gt;= 0.75). While on hold the civ still scouts
    /// from its capital and remembers the best sites.</item>
    /// <item><b>Parent.</b> Population &gt;= 30 (Orcs 22), food days &gt; 25, happiness &gt; 0.4 (largest first; ties by id).</item>
    /// <item><b>Settlers.</b> 6-10 people (never more than 25% of the parent, never the leader or soldiers in armies), as
    /// family units (couples first, then singles balancing the sexes). Units are chosen greedily for a balanced colony:
    /// the job mix it needs (food producers, builders, raw-resource workers, a craftsman), unemployed people, an average
    /// skill close to the parent's, and never the parent's only worker of a job.</item>
    /// <item><b>Site.</b> At most <see cref="MaxSiteCandidates"/> samples (<c>ctx.Rng</c>) in the annulus [spacing, 3*spacing]
    /// plus the remembered sites. Invalid: water/unbuildable, owned, too close to own (&lt; spacing) or foreign
    /// (&lt; 0.8*spacing) settlements, &lt; 40 free buildable tiles within radius 5, or within <see cref="ArmyAvoidRadius"/> of a
    /// hostile army (or the route there passing within <see cref="ArmyRouteRadius"/>). Score = habitability, fresh water,
    /// trees, stone/ore, fertility, plus future potential (resources and coast access the civ lacks, strategic chokepoints,
    /// hills) minus the distance penalty and proximity to rivals (hostile civs strongly, others by xenophobia).</item>
    /// <item><b>Founding.</b> <c>WorldOps.FoundSettlement</c>, moves the settlers and transfers starting supplies (Food 40,
    /// Wood 30, Stone 10, never draining the parent), then sets a 4-14 year cooldown scaled inversely by Expansionism.</item>
    /// </list>
    /// The best other sites (top <see cref="CivStrategyKeys.MaxRememberedSites"/>, distinct) are remembered in civ memory
    /// (<c>ai.site{i}.x/.y/.score/.tick</c>) and re-evaluated first next time. Parents without a valid site or settlers are
    /// skipped for <see cref="BlockedRetryTicks"/>. Every decision is logged at the capital with its main factors.
    /// God-spawned tribes are ordinary civilizations and expand under the same rules.
    /// </summary>
    public sealed class ExpansionSystem : ISimSystem
    {
        /// <summary>Tick interval of this system.</summary>
        public const int Interval = 48;
        /// <summary>Upper bound of site candidates sampled per expansion attempt.</summary>
        public const int MaxSiteCandidates = 400;
        /// <summary>Minimum parent population (all races except Orcs).</summary>
        public const int MinParentPopulation = 30;
        /// <summary>Minimum parent population for Orcs (fast breeders, expansionist).</summary>
        public const int MinParentPopulationOrc = 22;
        /// <summary>Parent must have more food days than this.</summary>
        public const float MinParentFoodDays = 25f;
        /// <summary>Parent must be happier than this.</summary>
        public const float MinParentHappiness = 0.4f;
        /// <summary>Preferred minimum settler count.</summary>
        public const int MinSettlers = 6;
        /// <summary>Maximum settler count.</summary>
        public const int MaxSettlers = 10;
        /// <summary>Colonies smaller than this are never sent (even if 25% of the parent is smaller than <see cref="MinSettlers"/>).</summary>
        public const int AbsoluteMinSettlers = 4;
        /// <summary>Maximum fraction of the parent population that may leave.</summary>
        public const float MaxSettlerFraction = 0.25f;
        /// <summary>Required free, unowned buildable tiles within <see cref="BuildableRadius"/>.</summary>
        public const int MinBuildableTiles = 40;
        /// <summary>Radius (Chebyshev) for the buildable-tile requirement.</summary>
        public const int BuildableRadius = 5;
        /// <summary>Radius (Chebyshev) for habitability/water/resource scoring.</summary>
        public const int ScoreRadius = 4;
        /// <summary>Minimum average habitability within <see cref="ScoreRadius"/> for a site to be acceptable.</summary>
        public const float MinAverageHabitability = 0.15f;
        /// <summary>Cooldown for Expansionism 1.</summary>
        public const float MinCooldownYears = 4f;
        /// <summary>Cooldown for Expansionism 0.</summary>
        public const float MaxCooldownYears = 14f;
        /// <summary>Food sent with the settlers (capped so the parent keeps <see cref="ParentFoodReserveDays"/>).</summary>
        public const float SupplyFood = 40f;
        /// <summary>Wood sent with the settlers (at most half of the parent's wood).</summary>
        public const float SupplyWood = 30f;
        /// <summary>Stone sent with the settlers (at most half of the parent's stone).</summary>
        public const float SupplyStone = 10f;
        /// <summary>Days of food the parent always keeps for its remaining population.</summary>
        public const float ParentFoodReserveDays = 15f;
        /// <summary>Ticks a parent is skipped after a failed attempt (no site / no settlers).</summary>
        public const int BlockedRetryTicks = SimTime.TicksPerYear * 2;
        /// <summary>Sites this close (tiles) to a hostile army are never settled.</summary>
        public const int ArmyAvoidRadius = 12;
        /// <summary>Sites whose straight route from the parent passes this close to a hostile army are never settled.</summary>
        public const int ArmyRouteRadius = 6;
        /// <summary>Radius of the ring scanned for chokepoints.</summary>
        public const int ChokeRingRadius = 6;
        /// <summary>Dynamic Expansionism at or above which a civ expands even during war or famine.</summary>
        public const float ExpansionDrivenTrait = 0.75f;
        /// <summary>Remembered sites older than this are forgotten.</summary>
        public const int SiteMemoryTicks = SimTime.TicksPerYear * 12;

        /// <summary>Hold reasons stored in <c>ai.expandHold</c>.</summary>
        public const int HoldNone = 0, HoldWar = 1, HoldFamine = 2;

        private const int TopK = CivStrategyKeys.MaxRememberedSites + 1;
        private static readonly Vec2i[] s_ring = BuildRing(ChokeRingRadius);

        // Reused buffers (no per-tick allocations).
        private readonly List<Vec2i> _ownCenters = new List<Vec2i>();
        private readonly List<Vec2i> _foreignCenters = new List<Vec2i>();
        private readonly List<Vec2i> _hostileCenters = new List<Vec2i>();
        private readonly List<Vec2i> _hostileArmies = new List<Vec2i>();
        private readonly List<List<Person>> _unitPool = new List<List<Person>>();
        private int _unitCount;
        private readonly Dictionary<int, int> _unitOfPerson = new Dictionary<int, int>();
        private readonly List<int> _couples = new List<int>();
        private readonly List<int> _singles = new List<int>();
        private readonly List<Person> _settlers = new List<Person>();
        private readonly int[] _jobCount = new int[Counts.Jobs];
        private readonly int[] _groupCat = new int[CategoryCount];
        private float _groupSkillSum, _parentMeanSkill;
        private int _groupAdults;
        private readonly int[] _topIdx = new int[TopK];
        private readonly float[] _topScore = new float[TopK];
        private readonly SiteEval[] _topEval = new SiteEval[TopK];
        private int _topCount;
        private readonly Vec2i[] _carried = new Vec2i[CivStrategyKeys.MaxRememberedSites];
        private readonly float[] _carriedScore = new float[CivStrategyKeys.MaxRememberedSites];
        private readonly long[] _carriedTick = new long[CivStrategyKeys.MaxRememberedSites];
        private int _carriedCount;
        private int _armyRejects;
        private readonly StringBuilder _sb = new StringBuilder(200);

        /// <summary>Score breakdown of one candidate site (for logs).</summary>
        private struct SiteEval
        {
            public float Habitability;
            public bool River, Coast, Choke, Hills;
            public PlacementRule NewFeatures;
            public float RivalDistance;
            public bool RivalHostile;
        }

        /// <inheritdoc />
        public string Name => "AI.Expansion";

        /// <inheritdoc />
        public int TickInterval => Interval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int maxSettlements = ctx.Config != null ? Math.Max(1, ctx.Config.MaxSettlementsPerCiv) : 24;
            var civs = ctx.State.Civilizations.Items;
            // Snapshot the count: civs created this tick (none expected) are handled next time.
            int civCount = civs.Count;
            for (int i = 0; i < civCount && i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                var race = StrategyHelpers.RaceOf(ctx.Db, civ.Race);
                if (race == null) continue;
                int count = StrategyHelpers.CountAliveSettlements(ctx.State, civ);
                if (count == 0 || count >= maxSettlements) continue;
                if (civ.Memory.TryGetValue(CivStrategyKeys.NextExpandTick, out float next) && ctx.Tick < MathUtil.Sanitize(next)) continue;
                TryExpand(ctx, civ, race);
            }
        }

        // ================================================================== expansion attempt
        private void TryExpand(SimContext ctx, Civilization civ, RaceDefinition race)
        {
            var state = ctx.State;
            var capital = StrategyHelpers.Capital(state, civ);
            int spacing = MathUtil.Clamp(race.MinSettlementSpacing, 4, 64);

            // Timing: no settlers during war or famine unless the civ is driven to expand.
            bool driven = StrategyHelpers.IsExpansionDriven(civ, ctx.Db, ExpansionDrivenTrait);
            int wars = StrategyHelpers.ActiveWars(state, civ.Id, out _);
            bool famine = StrategyHelpers.InFoodCrisis(state, civ, race, ctx.Tick, out float worstDays);
            int hold = driven ? HoldNone : wars > 0 ? HoldWar : famine ? HoldFamine : HoldNone;
            int prevHold = MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.ExpansionHold)));
            civ.Memory[CivStrategyKeys.ExpansionHold] = hold;
            if (hold != HoldNone)
            {
                // Keep scouting so the best sites are known when the crisis ends.
                int found = 0;
                if (capital != null)
                {
                    CollectCenters(state, civ.Id);
                    FindSite(ctx, civ, race, capital.Center, spacing, out _, out _);
                    found = RememberSites(civ, state.Map, ctx.Tick, -1);
                }
                if (hold != prevHold && capital != null)
                    capital.LogAi("Expansion: on hold (" + (hold == HoldWar ? "at war x" + wars : "famine, " + MathUtil.RoundToInt(Math.Min(999f, worstDays)) + " food days")
                        + "); scouted " + found + " site" + (found == 1 ? "" : "s") + BestSiteText(civ));
                return;
            }
            if (prevHold != HoldNone && capital != null)
                capital.LogAi("Expansion: resumed (" + (driven ? "expansion-driven" : "peace, food ok") + ")");

            var parent = ChooseParent(ctx, civ, race);
            if (parent == null) return;
            var logAt = capital ?? parent;

            if (!SelectSettlers(ctx, civ, parent, wars > 0))
            {
                parent.Memory[CivStrategyKeys.ExpandBlockedUntil] = ctx.Tick + BlockedRetryTicks;
                logAt.LogAi("Expansion: not enough willing settlers in " + parent.Name);
                return;
            }

            CollectCenters(state, civ.Id);
            var site = FindSite(ctx, civ, race, parent.Center, spacing, out float siteScore, out SiteEval eval);
            if (!site.IsValid)
            {
                _settlers.Clear();
                RememberSites(civ, state.Map, ctx.Tick, -1);
                parent.Memory[CivStrategyKeys.ExpandBlockedUntil] = ctx.Tick + BlockedRetryTicks;
                logAt.LogAi("Expansion: no valid site near " + parent.Name + (_armyRejects > 0 ? " (" + _armyRejects + " sites avoided: hostile armies)" : ""));
                return;
            }

            int parentPopBefore = parent.Population;
            var colony = ctx.Ops.FoundSettlement(civ, site, parent.Id);
            if (colony == null) { _settlers.Clear(); return; }
            RememberSites(civ, state.Map, ctx.Tick, 0); // the chosen site is taken; keep the runners-up

            int moved = 0;
            int food = 0, build = 0, res = 0, craft = 0, other = 0, adults = 0;
            float skill = 0f;
            for (int i = 0; i < _settlers.Count; i++)
            {
                var p = _settlers[i];
                if (p == null || !p.IsAlive || p.SettlementId != parent.Id) continue;
                if (p.IsAdult)
                {
                    adults++;
                    skill += MathUtil.Sanitize(p.Skill, 1f);
                    switch (Category(p.Job))
                    {
                        case CatFood: food++; break;
                        case CatBuild: build++; break;
                        case CatResource: res++; break;
                        case CatCraft: craft++; break;
                        default: other++; break;
                    }
                }
                ctx.Ops.MovePersonToSettlement(p, colony);
                if (p.SettlementId == colony.Id) moved++;
            }
            _settlers.Clear();

            float foodPerTick = StrategyHelpers.FoodPerTick(race);
            float reserve = parent.Population * foodPerTick * ParentFoodReserveDays;
            float foodSent = Transfer(parent, colony, ResourceType.Food, Math.Min(SupplyFood, Math.Max(0f, parent.Stockpile.Get(ResourceType.Food) - reserve)));
            float wood = Transfer(parent, colony, ResourceType.Wood, Math.Min(SupplyWood, parent.Stockpile.Get(ResourceType.Wood) * 0.5f));
            float stone = Transfer(parent, colony, ResourceType.Stone, Math.Min(SupplyStone, parent.Stockpile.Get(ResourceType.Stone) * 0.5f));

            float expansionism = StrategyHelpers.Trait(civ, ctx.Db, AITrait.Expansionism);
            float years = MathUtil.Lerp(MaxCooldownYears, MinCooldownYears, expansionism);
            float jitter = 0.9f + 0.2f * ctx.Rng.NextFloat();
            long cooldown = (long)(years * jitter * SimTime.TicksPerYear);
            cooldown = Math.Max((long)(MinCooldownYears * SimTime.TicksPerYear), Math.Min((long)(MaxCooldownYears * SimTime.TicksPerYear), cooldown));
            civ.Memory[CivStrategyKeys.NextExpandTick] = ctx.Tick + cooldown;
            civ.Memory[CivStrategyKeys.LastExpandTick] = ctx.Tick;
            parent.Memory[CivStrategyKeys.LastSettlersTick] = ctx.Tick;

            _sb.Length = 0;
            _sb.Append("Expansion: founded ").Append(colony.Name).Append(" at ").Append(site.ToString()).Append(" with ")
               .Append(moved).Append(" of ").Append(parentPopBefore).Append(" settlers from ").Append(parent.Name)
               .Append(" [food ").Append(food).Append(", build ").Append(build).Append(", res ").Append(res)
               .Append(", craft ").Append(craft).Append(", other ").Append(other)
               .Append("; skill ").Append(StrategyHelpers.Fmt1(adults > 0 ? skill / adults : 0f)).Append("]")
               .Append(" (site ").Append(StrategyHelpers.Fmt1(siteScore)).Append(": hab ").Append(StrategyHelpers.Fmt1(eval.Habitability));
            if (eval.River) _sb.Append(", river");
            if (eval.Coast) _sb.Append(", coast");
            AppendNewFeatures(eval.NewFeatures);
            if (eval.Choke) _sb.Append(", chokepoint");
            if (eval.RivalDistance < 1e6f) _sb.Append(eval.RivalHostile ? ", hostile " : ", rival ").Append(MathUtil.RoundToInt(eval.RivalDistance)).Append("t");
            if (_armyRejects > 0) _sb.Append(", avoided ").Append(_armyRejects).Append(" near armies");
            if (driven && (wars > 0 || famine)) _sb.Append(", expansion-driven despite ").Append(wars > 0 ? "war" : "famine");
            _sb.Append("; food ").Append(StrategyHelpers.Fmt1(foodSent)).Append(", wood ").Append(StrategyHelpers.Fmt1(wood))
               .Append(", stone ").Append(StrategyHelpers.Fmt1(stone)).Append(")");
            logAt.LogAi(_sb.ToString());
            if (logAt != parent) parent.LogAi("Expansion: sent " + moved + " settlers to found " + colony.Name);
            colony.LogAi("Founded by " + moved + " settlers from " + parent.Name);
            ctx.Log.Debug(civ.Name + " founded " + colony.Name + " from " + parent.Name);
        }

        private void AppendNewFeatures(PlacementRule f)
        {
            if ((f & PlacementRule.NearOre) != 0) _sb.Append(", +ore");
            if ((f & PlacementRule.NearStone) != 0) _sb.Append(", +stone");
            if ((f & PlacementRule.NearGold) != 0) _sb.Append(", +gold");
            if ((f & PlacementRule.NearCoal) != 0) _sb.Append(", +coal");
            if ((f & PlacementRule.NearOil) != 0) _sb.Append(", +oil");
            if ((f & PlacementRule.NearForest) != 0) _sb.Append(", +forest");
            if ((f & PlacementRule.Coastal) != 0) _sb.Append(", +coast access");
            if ((f & PlacementRule.Fertile) != 0) _sb.Append(", +fertile");
        }

        private static string BestSiteText(Civilization civ)
        {
            int n = MathUtil.Clamp(MathUtil.RoundToInt(civ.Mem(CivStrategyKeys.SiteCount)), 0, CivStrategyKeys.MaxRememberedSites);
            if (n == 0) return "";
            return ", best (" + MathUtil.RoundToInt(civ.Mem(CivStrategyKeys.SiteX(0))) + "," + MathUtil.RoundToInt(civ.Mem(CivStrategyKeys.SiteY(0)))
                + ") " + StrategyHelpers.Fmt1(civ.Mem(CivStrategyKeys.SiteScore(0)));
        }

        private static float Transfer(Settlement from, Settlement to, ResourceType type, float amount)
        {
            if (!(amount > 0f)) return 0f;
            float taken = from.Stockpile.ConsumeUpTo(type, amount);
            float stored = to.Stockpile.Add(type, taken);
            if (taken - stored > 1e-4f) from.Stockpile.Add(type, taken - stored); // colony full: return the rest
            return stored;
        }

        /// <summary>Eligible parent with the largest population (ties: lowest id), or null.</summary>
        private static Settlement ChooseParent(SimContext ctx, Civilization civ, RaceDefinition race)
        {
            int minPop = civ.Race == RaceId.Orc ? MinParentPopulationOrc : MinParentPopulation;
            Settlement best = null;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = ctx.State.Settlements.Get(ids[i]);
                if (!StrategyHelpers.IsOwnAlive(s, civ.Id)) continue;
                if (s.Population < minPop) continue;
                if (s.Memory.TryGetValue(CivStrategyKeys.ExpandBlockedUntil, out float until) && ctx.Tick < MathUtil.Sanitize(until)) continue;
                if (!(StrategyHelpers.FoodDays(s, race) > MinParentFoodDays)) continue;
                if (!(MathUtil.Sanitize(s.Happiness) > MinParentHappiness)) continue;
                if (best == null || s.Population > best.Population || (s.Population == best.Population && s.Id < best.Id)) best = s;
            }
            return best;
        }

        private void CollectCenters(WorldState state, int civId)
        {
            _ownCenters.Clear();
            _foreignCenters.Clear();
            _hostileCenters.Clear();
            _hostileArmies.Clear();
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.IsAlive) continue;
                if (s.CivilizationId == civId) _ownCenters.Add(s.Center);
                else
                {
                    _foreignCenters.Add(s.Center);
                    if (StrategyHelpers.IsHostile(state, civId, s.CivilizationId)) _hostileCenters.Add(s.Center);
                }
            }
            var armies = state.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (a == null || !a.IsAlive || a.CivilizationId == civId || a.State == ArmyState.Disbanding) continue;
                if (StrategyHelpers.IsHostile(state, civId, a.CivilizationId)) _hostileArmies.Add(a.Position);
            }
        }

        // ================================================================== site search
        /// <summary>
        /// Bounded, deterministic site search around <paramref name="origin"/> (remembered sites first, then random samples).
        /// Fills the top-K list; returns the best site or Vec2i.Invalid.
        /// </summary>
        private Vec2i FindSite(SimContext ctx, Civilization civ, RaceDefinition race, Vec2i origin, int spacing, out float bestScore, out SiteEval bestEval)
        {
            var map = ctx.State.Map;
            var rng = ctx.Rng;
            int minD = spacing, maxD = spacing * 3;
            int minD2 = minD * minD, maxD2 = maxD * maxD;
            var civFeatures = StrategyHelpers.CivFeatures(ctx.State, civ, ctx.Tick);
            float xen = StrategyHelpers.Trait(civ, ctx.Db, AITrait.Xenophobia);
            _topCount = 0;
            _armyRejects = 0;
            _carriedCount = 0;

            // Remembered sites: re-evaluated when in range, otherwise carried over unverified.
            int remembered = MathUtil.Clamp(MathUtil.RoundToInt(civ.Mem(CivStrategyKeys.SiteCount)), 0, CivStrategyKeys.MaxRememberedSites);
            for (int r = 0; r < remembered; r++)
            {
                long tick = (long)MathUtil.Sanitize(civ.Mem(CivStrategyKeys.SiteTick(r)));
                if (ctx.Tick - tick > SiteMemoryTicks || tick > ctx.Tick) continue;
                int x = MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.SiteX(r)), -1f));
                int y = MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.SiteY(r)), -1f));
                if (!map.InBounds(x, y)) continue;
                int d2 = (x - origin.X) * (x - origin.X) + (y - origin.Y) * (y - origin.Y);
                if (d2 >= minD2 && d2 <= maxD2) Consider(ctx, race, origin, x, y, d2, minD, maxD, spacing, civFeatures, xen);
                else
                {
                    _carried[_carriedCount] = new Vec2i(x, y);
                    _carriedScore[_carriedCount] = MathUtil.Sanitize(civ.Mem(CivStrategyKeys.SiteScore(r)));
                    _carriedTick[_carriedCount] = tick;
                    _carriedCount++;
                }
            }

            for (int k = 0; k < MaxSiteCandidates; k++)
            {
                int dx = rng.RangeInclusive(-maxD, maxD);
                int dy = rng.RangeInclusive(-maxD, maxD);
                int d2 = dx * dx + dy * dy;
                if (d2 < minD2 || d2 > maxD2) continue;
                Consider(ctx, race, origin, origin.X + dx, origin.Y + dy, d2, minD, maxD, spacing, civFeatures, xen);
            }
            if (_topCount == 0) { bestScore = 0f; bestEval = default(SiteEval); return Vec2i.Invalid; }
            bestScore = _topScore[0];
            bestEval = _topEval[0];
            return map.PosOf(_topIdx[0]);
        }

        private void Consider(SimContext ctx, RaceDefinition race, Vec2i origin, int x, int y, int d2, int minD, int maxD, int spacing,
            PlacementRule civFeatures, float xen)
        {
            var map = ctx.State.Map;
            int margin = BuildableRadius;
            if (x < margin || y < margin || x >= map.Width - margin || y >= map.Height - margin) return;
            int idx = map.Index(x, y);
            if (!map.Biomes[idx].IsBuildable() || map.Owner[idx] >= 0 || map.BuildingAt[idx] >= 0) return;
            if (StrategyHelpers.Habitability(race, map.Biomes[idx]) <= 0.05f) return;
            var p = new Vec2i(x, y);
            float ownMin2 = spacing * spacing;
            float foreignMin = 0.8f * spacing;
            if (!FarEnough(p, ownMin2, foreignMin * foreignMin)) return;
            if (!SafeFromArmies(origin, p)) { _armyRejects++; return; }
            if (!EvaluateSite(map, race, x, y, civFeatures, out float siteScore, out SiteEval ev)) return;

            float dist = (float)Math.Sqrt(d2);
            float penalty = maxD > minD ? (dist - minD) / (maxD - minD) * 0.35f : 0f;
            // Rivals: hostile neighbours are a liability; other foreigners mean border friction (xenophobia).
            float rival = float.MaxValue;
            for (int i = 0; i < _foreignCenters.Count; i++) rival = Math.Min(rival, p.DistanceTo(_foreignCenters[i]));
            float hostile = float.MaxValue;
            for (int i = 0; i < _hostileCenters.Count; i++) hostile = Math.Min(hostile, p.DistanceTo(_hostileCenters[i]));
            float rivalPenalty = 0f;
            float hostileRange = 3f * spacing, foreignRange = 2f * spacing;
            if (hostile < hostileRange) rivalPenalty += (1f - hostile / hostileRange) * 0.6f;
            if (rival < foreignRange) rivalPenalty += (1f - rival / foreignRange) * 0.2f * (0.5f + xen);
            ev.RivalDistance = rival;
            ev.RivalHostile = hostile <= rival + 1e-3f && hostile < float.MaxValue;
            float total = MathUtil.Sanitize(siteScore - penalty - rivalPenalty, float.NegativeInfinity);
            if (float.IsNegativeInfinity(total)) return;
            Offer(map, idx, total, ev, spacing);
        }

        /// <summary>Inserts a candidate into the distinct top-K list (sites closer than spacing keep only the better one).</summary>
        private void Offer(WorldMap map, int idx, float score, SiteEval ev, int spacing)
        {
            var p = map.PosOf(idx);
            for (int i = 0; i < _topCount; i++)
            {
                if (p.ChebyshevTo(map.PosOf(_topIdx[i])) >= spacing) continue;
                if (!Better(score, idx, _topScore[i], _topIdx[i])) return; // a better site nearby is already listed
            }
            // Remove every worse site close to this one.
            int w = 0;
            for (int i = 0; i < _topCount; i++)
            {
                if (p.ChebyshevTo(map.PosOf(_topIdx[i])) < spacing) continue;
                _topIdx[w] = _topIdx[i]; _topScore[w] = _topScore[i]; _topEval[w] = _topEval[i]; w++;
            }
            _topCount = w;
            int at = _topCount;
            for (int i = 0; i < _topCount; i++) if (Better(score, idx, _topScore[i], _topIdx[i])) { at = i; break; }
            if (at >= TopK) return;
            int last = Math.Min(_topCount, TopK - 1);
            for (int i = last; i > at; i--) { _topIdx[i] = _topIdx[i - 1]; _topScore[i] = _topScore[i - 1]; _topEval[i] = _topEval[i - 1]; }
            _topIdx[at] = idx; _topScore[at] = score; _topEval[at] = ev;
            if (_topCount < TopK) _topCount++;
        }

        private static bool Better(float s, int idx, float other, int otherIdx) => s > other + 1e-6f || (Math.Abs(s - other) <= 1e-6f && idx < otherIdx);

        /// <summary>
        /// Writes the top sites (skipping the first <paramref name="skip"/> + 1 when skip &gt;= 0, i.e. the site just settled)
        /// and carried-over remembered sites into civ memory. Returns how many sites are remembered.
        /// </summary>
        private int RememberSites(Civilization civ, WorldMap map, long tick, int skip)
        {
            int n = 0;
            for (int i = skip >= 0 ? skip + 1 : 0; i < _topCount && n < CivStrategyKeys.MaxRememberedSites; i++)
            {
                var p = map.PosOf(_topIdx[i]);
                WriteSite(civ, n++, p, _topScore[i], tick);
            }
            for (int c = 0; c < _carriedCount && n < CivStrategyKeys.MaxRememberedSites; c++)
            {
                bool dup = false;
                for (int i = 0; i < n; i++)
                    if (_carried[c].ChebyshevTo(new Vec2i(MathUtil.RoundToInt(civ.Mem(CivStrategyKeys.SiteX(i))), MathUtil.RoundToInt(civ.Mem(CivStrategyKeys.SiteY(i))))) < 3) dup = true;
                if (!dup) WriteSite(civ, n++, _carried[c], _carriedScore[c], _carriedTick[c]);
            }
            civ.Memory[CivStrategyKeys.SiteCount] = n;
            for (int i = n; i < CivStrategyKeys.MaxRememberedSites; i++)
            {
                civ.Memory.Remove(CivStrategyKeys.SiteX(i));
                civ.Memory.Remove(CivStrategyKeys.SiteY(i));
                civ.Memory.Remove(CivStrategyKeys.SiteScore(i));
                civ.Memory.Remove(CivStrategyKeys.SiteTick(i));
            }
            return n;
        }

        private static void WriteSite(Civilization civ, int i, Vec2i p, float score, long tick)
        {
            civ.Memory[CivStrategyKeys.SiteX(i)] = p.X;
            civ.Memory[CivStrategyKeys.SiteY(i)] = p.Y;
            civ.Memory[CivStrategyKeys.SiteScore(i)] = MathUtil.Sanitize(score);
            civ.Memory[CivStrategyKeys.SiteTick(i)] = tick;
        }

        private bool FarEnough(Vec2i p, float ownMin2, float foreignMin2)
        {
            for (int i = 0; i < _ownCenters.Count; i++) if (p.DistanceSqTo(_ownCenters[i]) < ownMin2) return false;
            for (int i = 0; i < _foreignCenters.Count; i++) if (p.DistanceSqTo(_foreignCenters[i]) < foreignMin2) return false;
            return true;
        }

        /// <summary>False when a hostile army is near the site or near the straight route from the parent.</summary>
        private bool SafeFromArmies(Vec2i origin, Vec2i site)
        {
            for (int i = 0; i < _hostileArmies.Count; i++)
            {
                var a = _hostileArmies[i];
                if (a.DistanceTo(site) <= ArmyAvoidRadius) return false;
                if (StrategyHelpers.DistanceToSegment(a, origin, site) <= ArmyRouteRadius) return false;
            }
            return true;
        }

        private const PlacementRule PotentialFeatures = PlacementRule.NearOre | PlacementRule.NearStone | PlacementRule.NearGold
            | PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.NearForest | PlacementRule.Coastal | PlacementRule.Fertile;

        /// <summary>
        /// Validates the buildable-area rule and scores the site: habitability + water + resources + fertility (present value)
        /// plus future potential (features the civ lacks, coast access, chokepoint, hills).
        /// </summary>
        private static bool EvaluateSite(WorldMap map, RaceDefinition race, int cx, int cy, PlacementRule civFeatures, out float score, out SiteEval ev)
        {
            score = 0f;
            ev = default(SiteEval);
            int buildable = 0, inner = 0, trees = 0;
            float habSum = 0f, fertSum = 0f;
            bool river = false, lakeOrCoast = false, coast = false, stone = false, ore = false;
            PlacementRule features = PlacementRule.None;
            for (int dy = -BuildableRadius; dy <= BuildableRadius; dy++)
                for (int dx = -BuildableRadius; dx <= BuildableRadius; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if (map.Owner[i] < 0 && map.IsFreeBuildable(i)) buildable++;
                    var f = map.Flags[i];
                    var b = map.Biomes[i];
                    features |= StrategyHelpers.FeaturesOf(f, b);
                    if ((f & TileFlags.StoneDeposit) != 0) stone = true;
                    if ((f & TileFlags.OreDeposit) != 0) ore = true;
                    if (dx < -ScoreRadius || dx > ScoreRadius || dy < -ScoreRadius || dy > ScoreRadius) continue;
                    inner++;
                    habSum += StrategyHelpers.Habitability(race, b);
                    if (b.IsLand()) fertSum += MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[i]));
                    if ((f & TileFlags.River) != 0) river = true;
                    if (b == Biome.Lake || (f & TileFlags.Coastal) != 0) lakeOrCoast = true;
                    if ((f & TileFlags.Coastal) != 0) coast = true;
                    if ((f & TileFlags.Trees) != 0) trees++;
                }
            if (buildable < MinBuildableTiles || inner == 0) return false;
            float avgHab = habSum / inner;
            if (avgHab < MinAverageHabitability) return false;
            float water = river ? 0.6f : (lakeOrCoast ? 0.4f : 0f);
            float wood = Math.Min(1f, trees / 12f) * 0.3f;
            float minerals = (stone ? 0.2f : 0f) + (ore ? 0.15f : 0f);
            float fertility = fertSum / inner * 0.2f;

            // Future potential: what this site adds to the civ.
            var newF = features & ~civFeatures & PotentialFeatures;
            float potential = 0f;
            if ((newF & PlacementRule.NearOre) != 0) potential += 0.25f;
            if ((newF & PlacementRule.NearStone) != 0) potential += 0.15f;
            if ((newF & PlacementRule.NearGold) != 0) potential += 0.2f;
            if ((newF & PlacementRule.NearCoal) != 0) potential += 0.15f;
            if ((newF & PlacementRule.NearOil) != 0) potential += 0.1f;
            if ((newF & PlacementRule.NearForest) != 0) potential += 0.15f;
            if ((newF & PlacementRule.Coastal) != 0) potential += 0.2f;   // first coast access: harbors, fishing, trade
            if ((newF & PlacementRule.Fertile) != 0) potential += 0.1f;
            if (coast) potential += 0.1f;
            bool choke = IsChokepoint(map, cx, cy);
            if (choke) potential += 0.2f;
            bool hills = map.Biomes[map.Index(cx, cy)] == Biome.Hills;
            if (hills) potential += 0.05f;

            score = MathUtil.Sanitize(avgHab + water + wood + minerals + fertility + potential);
            ev.Habitability = avgHab;
            ev.River = river;
            ev.Coast = coast;
            ev.Choke = choke;
            ev.Hills = hills;
            ev.NewFeatures = newF;
            ev.RivalDistance = float.MaxValue;
            return true;
        }

        /// <summary>
        /// Strategic chokepoint: on a ring around the site, passable land forms at least two separate arcs while
        /// 15-70% of the ring is impassable (water, mountains, snow, lava): a land bridge, pass or isthmus.
        /// </summary>
        private static bool IsChokepoint(WorldMap map, int cx, int cy)
        {
            int passable = 0, arcs = 0;
            bool prev = Passable(map, cx + s_ring[s_ring.Length - 1].X, cy + s_ring[s_ring.Length - 1].Y);
            for (int i = 0; i < s_ring.Length; i++)
            {
                bool cur = Passable(map, cx + s_ring[i].X, cy + s_ring[i].Y);
                if (cur) passable++;
                if (cur && !prev) arcs++;
                prev = cur;
            }
            float blocked = 1f - passable / (float)s_ring.Length;
            return arcs >= 2 && blocked >= 0.15f && blocked <= 0.7f;
        }

        private static bool Passable(WorldMap map, int x, int y)
        {
            if (!map.InBounds(x, y)) return false;
            var b = map.Biomes[map.Index(x, y)];
            return b.IsLand() && b != Biome.Mountain && b != Biome.Snow && b != Biome.Volcanic;
        }

        private static Vec2i[] BuildRing(int r)
        {
            var ring = new Vec2i[8 * r];
            int n = 0;
            for (int x = -r; x <= r; x++) ring[n++] = new Vec2i(x, -r);
            for (int y = -r + 1; y <= r; y++) ring[n++] = new Vec2i(r, y);
            for (int x = r - 1; x >= -r; x--) ring[n++] = new Vec2i(x, r);
            for (int y = r - 1; y > -r; y--) ring[n++] = new Vec2i(-r, y);
            return ring;
        }

        // ================================================================== settlers
        private const int CatIdle = 0, CatFood = 1, CatBuild = 2, CatResource = 3, CatCraft = 4, CatSoldier = 5, CatService = 6, CategoryCount = 7;
        /// <summary>Workers of each category a new colony wants (idle and soldiers: none needed).</summary>
        private static readonly int[] s_desired = { 0, 2, 1, 2, 1, 0, 1 };

        private static int Category(JobType j)
        {
            switch (j)
            {
                case JobType.None: return CatIdle;
                case JobType.Gatherer: case JobType.Hunter: case JobType.Fisher: case JobType.Farmer: case JobType.Herder: return CatFood;
                case JobType.Builder: case JobType.Laborer: return CatBuild;
                case JobType.Woodcutter: case JobType.Quarrier: case JobType.Miner: return CatResource;
                case JobType.Smith: case JobType.Craftsman: case JobType.Weaver: case JobType.Engineer: return CatCraft;
                case JobType.Soldier: return CatSoldier;
                default: return CatService;
            }
        }

        /// <summary>Fills <see cref="_settlers"/>; false when a viable group cannot be formed.</summary>
        private bool SelectSettlers(SimContext ctx, Civilization civ, Settlement parent, bool atWar)
        {
            _settlers.Clear();
            int pop = parent.Population;
            int cap = (int)Math.Floor(pop * MaxSettlerFraction);
            if (cap < AbsoluteMinSettlers) return false;
            int target = Math.Min(ctx.Rng.RangeInclusive(MinSettlers, MaxSettlers), cap);
            int minAccept = Math.Min(MinSettlers, cap);
            BuildUnits(ctx.State, civ, parent);
            CountParentJobs(ctx.State, parent);

            _couples.Clear();
            _singles.Clear();
            for (int u = 0; u < _unitCount; u++)
            {
                var unit = _unitPool[u];
                if (unit.Count >= 2 && unit[1].IsAdult && unit[1].SpouseId == unit[0].Id) _couples.Add(u); else _singles.Add(u);
            }
            // Shuffle first so equally valued units vary between attempts (deterministic through ctx.Rng).
            ctx.Rng.Shuffle(_couples);
            ctx.Rng.Shuffle(_singles);
            Array.Clear(_groupCat, 0, _groupCat.Length);
            _groupSkillSum = 0f;
            _groupAdults = 0;

            int total = 0, females = 0, males = 0;
            // Couples first (stable colonies), the most useful couple each round.
            while (total < target)
            {
                int pick = PickBest(_couples, target - total, null, atWar);
                if (pick < 0) break;
                AddUnit(_unitPool[_couples[pick]], ref total, ref females, ref males);
                _couples[pick] = -1;
            }
            // Singles (with their children), keeping the adult sex ratio balanced.
            while (total < target)
            {
                Sex wanted = females <= males ? Sex.Female : Sex.Male;
                int pick = PickBest(_singles, target - total, wanted, atWar);
                if (pick < 0) pick = PickBest(_singles, target - total, null, atWar);
                if (pick < 0) break;
                AddUnit(_unitPool[_singles[pick]], ref total, ref females, ref males);
                _singles[pick] = -1;
            }

            for (int u = 0; u < _unitCount; u++) _unitPool[u].Clear(); // drop references
            _unitOfPerson.Clear();
            if (total < minAccept || females + males == 0)
            {
                _settlers.Clear();
                return false;
            }
            return true;
        }

        /// <summary>Index (into <paramref name="units"/>) of the most valuable unit that fits, or -1. Ties: list order.</summary>
        private int PickBest(List<int> units, int room, Sex? sex, bool atWar)
        {
            int best = -1;
            float bestValue = float.NegativeInfinity;
            for (int i = 0; i < units.Count; i++)
            {
                int u = units[i];
                if (u < 0) continue;
                var unit = _unitPool[u];
                if (unit.Count > room) continue;
                if (sex.HasValue && unit[0].Sex != sex.Value) continue;
                float v = UnitValue(unit, atWar);
                if (v > bestValue + 1e-6f) { bestValue = v; best = i; }
            }
            return best;
        }

        /// <summary>How much a family unit helps a balanced colony (job mix, skill balance) without crippling the parent.</summary>
        private float UnitValue(List<Person> unit, bool atWar)
        {
            float v = 0f;
            float skillSum = _groupSkillSum;
            int adults = _groupAdults;
            int c0 = 0, c1 = 0; // categories already counted inside this unit (max 2 adults)
            int seen = 0;
            for (int i = 0; i < unit.Count; i++)
            {
                var p = unit[i];
                if (!p.IsAdult) continue;
                int cat = Category(p.Job);
                int have = _groupCat[cat] + (seen > 0 && c0 == cat ? 1 : 0) + (seen > 1 && c1 == cat ? 1 : 0);
                if (cat == CatIdle) v += 0.4f;                      // unemployed: useful there, not missed here
                else if (have < s_desired[cat]) v += 1f;            // a job the colony needs
                else v += 0.15f;
                if (cat == CatSoldier) v -= atWar ? 1.5f : 0.8f;    // defenders stay home
                int job = (int)p.Job;
                if (p.Job != JobType.None && job >= 0 && job < _jobCount.Length && _jobCount[job] <= 1) v -= 1.2f; // the parent's only one
                if (seen == 0) c0 = cat; else c1 = cat;
                seen++;
                skillSum += MathUtil.Clamp(MathUtil.Sanitize(p.Skill, 1f), 0.3f, 2f);
                adults++;
            }
            if (adults > 0) v -= Math.Abs(skillSum / adults - _parentMeanSkill) * 0.8f; // keep the colony's skill balanced
            return v;
        }

        private void CountParentJobs(WorldState state, Settlement parent)
        {
            Array.Clear(_jobCount, 0, _jobCount.Length);
            float skill = 0f;
            int adults = 0;
            var residents = parent.ResidentIds;
            for (int r = 0; r < residents.Count; r++)
            {
                var p = state.People.Get(residents[r]);
                if (p == null || !p.IsAlive || !p.IsAdult) continue;
                int j = (int)p.Job;
                if (j >= 0 && j < _jobCount.Length) _jobCount[j]++;
                skill += MathUtil.Clamp(MathUtil.Sanitize(p.Skill, 1f), 0.3f, 2f);
                adults++;
            }
            _parentMeanSkill = adults > 0 ? skill / adults : 1f;
        }

        private void AddUnit(List<Person> unit, ref int total, ref int females, ref int males)
        {
            for (int i = 0; i < unit.Count; i++)
            {
                var p = unit[i];
                _settlers.Add(p);
                if (p.IsAdult)
                {
                    if (p.Sex == Sex.Female) females++; else males++;
                    _groupCat[Category(p.Job)]++;
                    _groupSkillSum += MathUtil.Clamp(MathUtil.Sanitize(p.Skill, 1f), 0.3f, 2f);
                    _groupAdults++;
                }
            }
            total += unit.Count;
        }

        /// <summary>Groups the parent's eligible residents into family units (couple or single adult + their children).</summary>
        private void BuildUnits(WorldState state, Civilization civ, Settlement parent)
        {
            _unitCount = 0;
            _unitOfPerson.Clear();
            int leaderId = civ.LeaderId;
            var people = state.People;
            var residents = parent.ResidentIds;

            for (int r = 0; r < residents.Count; r++)
            {
                var p = people.Get(residents[r]);
                if (p == null || !p.IsAlive || p.Stage != LifeStage.Adult || p.Id == leaderId || p.ArmyId >= 0) continue;
                if (p.SettlementId != parent.Id || _unitOfPerson.ContainsKey(p.Id)) continue;
                var spouse = p.SpouseId >= 0 ? people.Get(p.SpouseId) : null;
                bool spouseHere = spouse != null && spouse.IsAlive && spouse.SettlementId == parent.Id;
                if (spouseHere)
                {
                    // Couples travel together or not at all.
                    if (spouse.Id == leaderId || spouse.ArmyId >= 0 || !spouse.IsAdult || _unitOfPerson.ContainsKey(spouse.Id)) continue;
                    var unit = NewUnit();
                    unit.Add(p);
                    unit.Add(spouse);
                    _unitOfPerson[p.Id] = _unitCount - 1;
                    _unitOfPerson[spouse.Id] = _unitCount - 1;
                }
                else
                {
                    var unit = NewUnit();
                    unit.Add(p);
                    _unitOfPerson[p.Id] = _unitCount - 1;
                }
            }

            // Children follow their parents, but only if every parent living here is in the same unit.
            for (int r = 0; r < residents.Count; r++)
            {
                var c = people.Get(residents[r]);
                if (c == null || !c.IsAlive || c.Stage != LifeStage.Child || c.Id == leaderId || c.ArmyId >= 0 || c.SettlementId != parent.Id) continue;
                int unitM = UnitOfParent(people, parent.Id, c.MotherId, out bool motherHere);
                int unitF = UnitOfParent(people, parent.Id, c.FatherId, out bool fatherHere);
                if ((motherHere && unitM < 0) || (fatherHere && unitF < 0)) continue;
                int u = unitM >= 0 ? unitM : unitF;
                if (u < 0 || (unitM >= 0 && unitF >= 0 && unitM != unitF)) continue;
                _unitPool[u].Add(c);
            }
        }

        private int UnitOfParent(EntityStore<Person> people, int settlementId, int parentId, out bool here)
        {
            here = false;
            if (parentId < 0) return -1;
            var p = people.Get(parentId);
            here = p != null && p.IsAlive && p.SettlementId == settlementId;
            if (!here) return -1;
            return _unitOfPerson.TryGetValue(parentId, out int u) ? u : -1;
        }

        private List<Person> NewUnit()
        {
            if (_unitCount == _unitPool.Count) _unitPool.Add(new List<Person>(4));
            var unit = _unitPool[_unitCount++];
            unit.Clear();
            return unit;
        }
    }
}
