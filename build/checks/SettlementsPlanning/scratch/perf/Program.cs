using System;
using System.Collections.Generic;
using System.Diagnostics;
using GodSim.Core;
using GodSim.Core.Settlements;

static class Program
{
    static GameDatabase Db()
    {
        var db = new GameDatabase();
        db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5, Desirability = 0.1f });
        db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, Width = 3, Height = 3, Placement = PlacementRule.Fertile, Job = JobType.Farmer, JobSlots = 3 });
        db.Register(new BuildingDefinition { Type = BuildingType.Well, Name = "Well", Category = BuildingCategory.Infrastructure });
        db.Register(new BuildingDefinition { Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource, Width = 2, Height = 2, Placement = PlacementRule.NearOre, Desirability = -0.4f, Job = JobType.Miner, JobSlots = 4 });
        GodSim.Tests.Shared.TestDb.FillMissing(db);
        db.Freeze();
        return db;
    }

    static void CompleteAll(SimContext ctx, Settlement s)
    {
        foreach (int id in s.BuildingIds.ToArray()) { var b = ctx.State.Buildings.Get(id); if (b != null && !b.IsComplete) ctx.Ops.CompleteBuilding(b); }
    }

    static void Fill(SimContext ctx, List<Settlement> towns, int orders)
    {
        foreach (var s in towns)
            while (s.BuildQueue.Count < orders) s.BuildQueue.Add(new BuildOrder { Type = BuildingType.House, Priority = 1f, RequestedTick = ctx.State.Tick });
    }

    static double[] Measure(TownPlannerSystem planner, SimContext ctx, List<Settlement> towns, int runs, int orders)
    {
        var sw = new Stopwatch();
        double total = 0, worst = 0;
        var workProp = typeof(TownPlannerSystem).GetProperty("LastRunWork");
        var candProp = typeof(TownPlannerSystem).GetProperty("LastRunCandidates");
        long work = 0, cand = 0, placed = 0; string worstInfo = "";
        for (int r = 0; r < runs; r++)
        {
            Fill(ctx, towns, orders);
            int before = Buildings(towns);
            ctx.State.Tick += 2;
            ctx.DeltaTicks = 2;
            int gc0 = GC.CollectionCount(0);
            sw.Restart();
            planner.Tick(ctx);
            sw.Stop();
            ctx.Ops.FlushRemovals();
            double ms = sw.Elapsed.TotalMilliseconds;
            int w = workProp != null ? (int)workProp.GetValue(planner) : -1, c = candProp != null ? (int)candProp.GetValue(planner) : -1;
            int p = Buildings(towns) - before;
            work += w; cand += c; placed += p;
            if (ms > worst) { worst = ms; worstInfo = "work " + w + ", candidates " + c + ", placed " + p + ", gc0 " + (GC.CollectionCount(0) - gc0); }
            total += ms;
            foreach (var s in towns) CompleteAll(ctx, s);
        }
        Console.WriteLine("    per run: work " + (work / runs) + ", candidates " + (cand / runs) + ", placed " + (placed / (double)runs).ToString("0.0") + "; worst run: " + worstInfo);
        return new[] { total / runs, worst };
    }

#if NEWCODE
    static void Calibrate(SimContext ctx, List<Settlement> towns)
    {
        var zoning = new ZoningMap();
        var field = new AccessField();
        var sw = new Stopwatch();
        long ztiles = 0, ftiles = 0; double zms = 0, fms = 0;
        for (int rep = 0; rep < 3; rep++)
            foreach (var s in towns)
            {
                int radius = TownPlannerSystem.PlanningRadiusOf(s);
                sw.Restart(); zoning.Build(ctx.State, ctx.Db, s, radius); sw.Stop(); zms += sw.Elapsed.TotalMilliseconds; ztiles += zoning.LastBuildTiles;
                sw.Restart(); field.Build(ctx.State, s, radius + ZoningMap.Margin); sw.Stop(); fms += sw.Elapsed.TotalMilliseconds;
                int side = 2 * (radius + ZoningMap.Margin) + 1; ftiles += side * side;
            }
        Console.WriteLine("    zoning: " + (zms * 1e6 / ztiles).ToString("0.00") + " ns/tile; field: " + (fms * 1e6 / ftiles).ToString("0.00") + " ns/window tile");
        // candidate evaluation cost: probe + rules on a typical 1x1 / 3x3 site
        var probe = default(SiteProbe);
        var map = ctx.State.Map;
        int n = 0; sw.Restart();
        foreach (var s in towns)
            for (int dy = -8; dy <= 8; dy++) for (int dx = -8; dx <= 8; dx++)
            {
                var o = new Vec2i(s.Center.X + dx, s.Center.Y + dy);
                if (!ctx.Ops.CanPlace(o, 3, 3)) continue;
                PlacementRules.FootprintClaimable(map, s.Id, o, 3, 3);
                PlacementRules.Probe(map, o, 3, 3, ref probe);
                PlacementRules.MeetsRequirements(PlacementRule.Fertile, in probe);
                SiteScorer.Score(map, s, ctx.Db.Race(s.Race), ctx.Db.Building(BuildingType.Farm), o, in probe, zoning, 8, 0f);
                n++;
            }
        sw.Stop();
        Console.WriteLine("    full 3x3 evaluation: " + (sw.Elapsed.TotalMilliseconds * 1e6 / Math.Max(1, n)).ToString("0") + " ns (" + n + " sites)");
    }
#endif

    static int Buildings(List<Settlement> towns) { int n = 0; foreach (var s in towns) n += s.BuildingIds.Count; return n; }

    static void Main(string[] args)
    {
        var db = Db();
        var ctx = GodSim.Tests.Shared.TestWorlds.CreateContext(db, 320, 99);
        ctx.Config.StrictMode = false;
        var towns = new List<Settlement>();
        for (int k = 0; k < 30; k++)
        {
            var at = new Vec2i(28 + (k % 6) * 52, 28 + (k / 6) * 60);
            towns.Add(GodSim.Tests.Shared.TestWorlds.AddSettlement(ctx, (RaceId)(k % 5), at, 60, out _));
        }
        var planner = new TownPlannerSystem();
        planner.Initialize(ctx);
        // Grow every town to ~28 buildings with the planner itself.
        int guard = 0;
        while (Buildings(towns) < 30 * 28 && guard++ < 2000)
        {
            Fill(ctx, towns, 3);
            ctx.State.Tick += 2; planner.Tick(ctx); ctx.Ops.FlushRemovals();
            foreach (var s in towns) CompleteAll(ctx, s);
        }
        Measure(planner, ctx, towns, 20, 3); // warm-up (JIT)
        var a = Measure(planner, ctx, towns, 100, 3);
        Console.WriteLine("(a) 30 towns, " + Buildings(towns) + " buildings, 3 House orders each: avg " + a[0].ToString("0.000") + " ms/run (" + (a[0] / 2).ToString("0.000") + " ms/tick), worst " + a[1].ToString("0.000") + " ms");

        // (b) same towns at TerritoryRadius 20, topped up to >= 60 buildings each, 5 orders.
        foreach (var s in towns) ctx.Ops.ClaimTerritory(s, 20);
        guard = 0;
        while (Buildings(towns) < 30 * 60 && guard++ < 4000)
        {
            Fill(ctx, towns, 5);
            ctx.State.Tick += 2; planner.Tick(ctx); ctx.Ops.FlushRemovals();
            foreach (var s in towns) CompleteAll(ctx, s);
        }
        Measure(planner, ctx, towns, 20, 5);
        var b = Measure(planner, ctx, towns, 100, 5);
#if NEWCODE
        Calibrate(ctx, towns);
#endif
        Console.WriteLine("(b) radius 20, " + Buildings(towns) + " buildings, 5 House orders each: avg " + b[0].ToString("0.000") + " ms/run (" + (b[0] / 2).ToString("0.000") + " ms/tick), worst " + b[1].ToString("0.000") + " ms");
    }
}
