using System;
using System.Collections.Generic;
using System.Globalization;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Finishes buildings that the town planner has already placed (<see cref="Building.IsComplete"/> = false).
    /// Every tick, per alive settlement:
    /// <list type="number">
    /// <item>Gathers incomplete, non-destroyed sites in priority order: Town Center, then dwellings while
    /// <see cref="Settlement.Homeless"/> &gt; 0, then everything else; ties in placement order (oldest first).</item>
    /// <item>Pays each unpaid site atomically with <see cref="Stockpile.TryPay"/>. Unaffordable sites wait; the
    /// wait is tracked in <c>Memory["build.wait.&lt;id&gt;"]</c> (ticks) and a site unpaid for more than three
    /// years is destroyed with <see cref="DestroyCause.Abandoned"/>.</item>
    /// <item>Computes labor: alive adult builders weighted by <see cref="Person.Skill"/>, plus 0.3 per idle
    /// unemployed adult (at most 2 x builders + 3 of them), times <see cref="ModifierSet.BuildSpeed"/> and
    /// <see cref="SimContext.DeltaTicks"/>. Labor goes to paid sites in priority order, at most four builders
    /// per footprint tile; progress += labor / BuildWork, and a site at 1 is completed through WorldOps.</item>
    /// <item>Writes <see cref="Settlement.DesiredBuilders"/> = clamp(2 x paid sites + 1, 0, adults / 3), or 0
    /// when nothing is under construction.</item>
    /// </list>
    /// Wonders get a chronicle entry when their construction begins (cost paid).
    /// </summary>
    public sealed class ConstructionSystem : ISimSystem
    {
        /// <summary>Settlement memory key prefix: ticks an unpaid site has waited ("build.wait.&lt;buildingId&gt;").</summary>
        public const string WaitKeyPrefix = "build.wait.";
        /// <summary>Settlement memory key: number of paid sites currently under construction (absent when 0).</summary>
        public const string ActiveSitesKey = "build.sitesActive";
        /// <summary>Settlement memory key: number of placed sites still waiting for resources (absent when 0).</summary>
        public const string WaitingSitesKey = "build.sitesWaiting";
        /// <summary>An unpaid site waiting longer than this many ticks (three years) is abandoned.</summary>
        public const int MaxWaitTicks = 3 * SimTime.TicksPerYear;
        /// <summary>Labor contributed by each idle unemployed adult (builders count their Skill).</summary>
        public const float IdleAdultLabor = 0.3f;
        /// <summary>Extra idle helpers allowed on top of 2 x builders.</summary>
        public const int IdleHelperBase = 3;
        /// <summary>Maximum builders (labor units) one footprint tile can absorb per tick.</summary>
        public const int BuildersPerTile = 4;
        /// <summary>Sanity clamp for the civilization build-speed multiplier.</summary>
        public const float MaxBuildSpeed = 20f;
        /// <summary>Sanity clamp for a single person's skill weight.</summary>
        public const float MaxSkill = 3f;

        private const int WaitKeyCacheLimit = 4096;

        private struct Site
        {
            public Building Building;
            public BuildingDefinition Def;
            public int Priority;
        }

        private readonly List<Site> _sites = new List<Site>();
        private readonly List<string> _staleKeys = new List<string>();
        private readonly Dictionary<int, string> _waitKeys = new Dictionary<int, string>();

        /// <inheritdoc />
        public string Name => "Construction";

        /// <inheritdoc />
        public int TickInterval => 1;

        /// <summary>Settlement memory key that tracks how long the given unpaid site has waited.</summary>
        public static string WaitKeyFor(int buildingId) => WaitKeyPrefix + buildingId.ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            _sites.Clear();
            _staleKeys.Clear();
            _waitKeys.Clear();
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int dt = Math.Max(0, ctx.DeltaTicks);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                UpdateSettlement(ctx, s, dt);
            }
            _sites.Clear();
        }

        /// <summary>
        /// Raw labor (builder-equivalents per tick, before BuildSpeed and DeltaTicks) available to a settlement:
        /// the summed <see cref="Person.Skill"/> of alive adult builders not in an army, plus
        /// <see cref="IdleAdultLabor"/> per idle unemployed adult, capped at 2 x builders + 3 helpers.
        /// </summary>
        /// <param name="state">World state.</param>
        /// <param name="s">Settlement.</param>
        /// <param name="adults">Receives the number of alive adult residents (elders included).</param>
        public static float ComputeLabor(WorldState state, Settlement s, out int adults)
        {
            adults = 0;
            if (state == null || s == null) return 0f;
            float skill = 0f;
            int builders = 0, idle = 0;
            var ids = s.ResidentIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var p = state.People.Get(ids[k]);
                if (p == null || !p.IsAlive || !p.IsAdult || p.SettlementId != s.Id) continue;
                adults++;
                if (p.ArmyId >= 0) continue; // away with an army
                if (p.Job == JobType.Builder)
                {
                    builders++;
                    skill += MathUtil.Clamp(MathUtil.Sanitize(p.Skill, 1f), 0f, MaxSkill);
                }
                else if (p.Job == JobType.None) idle++;
            }
            int helpers = Math.Min(idle, 2 * builders + IdleHelperBase);
            return MathUtil.Sanitize(skill + IdleAdultLabor * helpers, 0f);
        }

        private void UpdateSettlement(SimContext ctx, Settlement s, int dt)
        {
            var state = ctx.State;
            if (ConstructionShared.Mod(state.Tick + s.Id, SimTime.TicksPerYear) < Math.Max(1, dt)) SweepStaleWaitKeys(state, s);

            GatherSites(ctx, s);
            if (_sites.Count == 0)
            {
                s.DesiredBuilders = 0;
                s.Memory.Remove(ActiveSitesKey);
                s.Memory.Remove(WaitingSitesKey);
                return;
            }

            // ---- 1. pay for sites (atomic), track waits, abandon long-unfunded sites
            int paid = 0, waiting = 0;
            for (int i = 0; i < _sites.Count; i++)
            {
                var site = _sites[i];
                var b = site.Building;
                if (b.IsDestroyed) continue;
                if (b.CostPaid) { paid++; continue; }
                if (s.Stockpile.TryPay(site.Def.Cost))
                {
                    b.CostPaid = true;
                    ClearWait(s, b.Id);
                    paid++;
                    if (site.Def.IsWonder)
                        ctx.Ops.AddChronicle(ChronicleCategory.Building,
                            "Work began on the wonder " + ConstructionShared.NameOf(site.Def, b) + " in " + s.Name + ".",
                            s.CivilizationId, s.Id, b.Center, 2);
                    continue;
                }
                string key = WaitKey(b.Id);
                float waited = Math.Max(0f, MathUtil.Sanitize(s.Mem(key), 0f)) + dt;
                if (waited > MaxWaitTicks)
                {
                    ClearWait(s, b.Id);
                    ctx.Ops.DestroyBuilding(b, DestroyCause.Abandoned);
                    string line = "Abandoned unfunded " + ConstructionShared.NameOf(site.Def, b) + " site after 3 years";
                    s.LogAi(line);
                    ctx.Log?.Debug(s.Name + ": " + line);
                    continue;
                }
                s.Memory[key] = waited;
                waiting++;
            }

            ConstructionShared.SetOrClear(s, ActiveSitesKey, paid);
            ConstructionShared.SetOrClear(s, WaitingSitesKey, waiting);
            if (paid == 0)
            {
                s.DesiredBuilders = 0;
                return;
            }

            // ---- 2. labor
            float labor = ComputeLabor(state, s, out int adults);
            s.DesiredBuilders = MathUtil.Clamp(2 * paid + 1, 0, adults / 3);

            var civ = ConstructionShared.CivOf(state, s);
            float speed = civ != null ? MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.BuildSpeed, 1f), 0f, MaxBuildSpeed) : 1f;
            float perUnit = speed * dt;
            float budget = labor;

            // ---- 3. distribute labor by priority, capped per footprint tile
            for (int i = 0; i < _sites.Count; i++)
            {
                var site = _sites[i];
                var b = site.Building;
                if (b.IsDestroyed || b.IsComplete || !b.CostPaid) continue;
                float progress = MathUtil.Clamp01(MathUtil.Sanitize(b.ConstructionProgress, 0f));
                if (budget > 0f && perUnit > 0f)
                {
                    float cap = BuildersPerTile * ConstructionShared.Footprint(b);
                    float assigned = Math.Min(budget, cap);
                    budget -= assigned;
                    float work = site.Def.BuildWork > MathUtil.Epsilon ? site.Def.BuildWork : 1f;
                    progress = MathUtil.Clamp01(MathUtil.Sanitize(progress + assigned * perUnit / work, progress));
                }
                b.ConstructionProgress = progress;
                if (progress >= 1f - 1e-5f) Complete(ctx, s, b);
            }
        }

        private static void Complete(SimContext ctx, Settlement s, Building b)
        {
            ctx.Ops.CompleteBuilding(b);
            if (b.IsComplete && !b.IsDestroyed && b.Type == BuildingType.TownCenter && !ConstructionShared.HasValidTownCenter(ctx.State, s))
                s.TownCenterId = b.Id;
        }

        private void GatherSites(SimContext ctx, Settlement s)
        {
            _sites.Clear();
            bool homeless = s.Homeless > 0;
            var ids = s.BuildingIds;
            var buildings = ctx.State.Buildings;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = buildings.Get(ids[k]);
                if (b == null || b.IsDestroyed || b.IsComplete || b.SettlementId != s.Id) continue;
                if (!ctx.Db.TryGetBuilding(b.Type, out var def)) continue;
                var site = new Site
                {
                    Building = b,
                    Def = def,
                    Priority = b.Type == BuildingType.TownCenter ? 0 : (homeless && def.IsDwelling ? 1 : 2),
                };
                // stable insertion sort by (priority, id); id order == placement order
                int pos = _sites.Count;
                _sites.Add(site);
                while (pos > 0 && Before(site, _sites[pos - 1]))
                {
                    _sites[pos] = _sites[pos - 1];
                    pos--;
                }
                _sites[pos] = site;
            }
        }

        private static bool Before(Site a, Site b) =>
            a.Priority < b.Priority || (a.Priority == b.Priority && a.Building.Id < b.Building.Id);

        private string WaitKey(int buildingId)
        {
            if (_waitKeys.TryGetValue(buildingId, out var key)) return key;
            if (_waitKeys.Count >= WaitKeyCacheLimit) _waitKeys.Clear();
            key = WaitKeyFor(buildingId);
            _waitKeys[buildingId] = key;
            return key;
        }

        private void ClearWait(Settlement s, int buildingId)
        {
            if (_waitKeys.TryGetValue(buildingId, out var key)) _waitKeys.Remove(buildingId);
            else key = WaitKeyFor(buildingId);
            s.Memory.Remove(key);
        }

        /// <summary>Removes wait keys whose site was paid, completed, destroyed or moved by another system.</summary>
        private void SweepStaleWaitKeys(WorldState state, Settlement s)
        {
            _staleKeys.Clear();
            foreach (var kv in s.Memory)
            {
                string key = kv.Key;
                if (key == null || !key.StartsWith(WaitKeyPrefix, StringComparison.Ordinal)) continue;
                bool ok = int.TryParse(key.Substring(WaitKeyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int id);
                var b = ok ? state.Buildings.Get(id) : null;
                if (b == null || b.IsDestroyed || b.IsComplete || b.CostPaid || b.SettlementId != s.Id) _staleKeys.Add(key);
            }
            for (int i = 0; i < _staleKeys.Count; i++) s.Memory.Remove(_staleKeys[i]);
            _staleKeys.Clear();
        }
    }
}
