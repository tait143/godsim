using System;
using System.Collections.Generic;
using GodSim.Bench.Harness;
using GodSim.Core;

namespace GodSim.Bench.Benchmarks
{
    /// <summary>
    /// Micro-benchmarks of the shared Core/Contracts primitives. These depend on nothing but Contracts,
    /// so they run even when no gameplay module compiles.
    /// </summary>
    public static class ContractsBenchmarks
    {
        public const string Category = "contracts";

        public static IEnumerable<BenchCase> Cases()
        {
            yield return new BenchCase { Category = Category, Name = "pathfinder", Description = "GridPathfinder: 128-tile paths, obstacle field, unreachable worst case", Run = Pathfinder };
            yield return new BenchCase { Category = Category, Name = "entitystore", Description = "EntityStore<T> add/get/remove/iterate at 100k", Run = EntityStoreOps };
            yield return new BenchCase { Category = Category, Name = "stockpile", Description = "Stockpile add/consume/pay/roll statistics", Run = StockpileOps };
            yield return new BenchCase { Category = Category, Name = "eventbus", Description = "EventBus.Publish with 0 and 5 subscribers (must allocate 0 bytes)", Run = EventBusOps };
            yield return new BenchCase { Category = Category, Name = "rng", Description = "PCG32 Rng throughput", Run = RngOps };
            yield return new BenchCase { Category = Category, Name = "worldops", Description = "WorldOps.CreatePerson / KillPerson scaling with settlement size", Run = WorldOpsOps };
        }

        // =========================================================================== pathfinder
        private static IEnumerable<BenchResult> Pathfinder(BenchOptions o)
        {
            const int W = 256, H = 256;
            int iters = o.Q(60, 250), warm = o.Q(10, 30);
            var path = new List<Vec2i>(1024);
            var pf = new GridPathfinder(W, H);

            // --- open field (cost 1 everywhere)
            var open = new float[W * H];
            for (int i = 0; i < open.Length; i++) open[i] = 1f;
            Func<int, float> openCost = i => open[i];

            yield return PathCase(o, "path128.open.straight", pf, path, new Vec2i(64, 128), new Vec2i(192, 128), openCost, true, 20000, warm, iters);
            yield return PathCase(o, "path128.open.diagonal", pf, path, new Vec2i(64, 64), new Vec2i(192, 192), openCost, true, 20000, warm, iters);
            yield return PathCase(o, "path128.open.4way", pf, path, new Vec2i(64, 128), new Vec2i(192, 128), openCost, false, 20000, warm, iters);

            // --- obstacle fields (deterministic from seed): 22% walls with unit cost (budget case), and the same walls
            //     with random 1..3 cost multipliers (terrain-like; shows how a unit-cost octile heuristic degrades).
            Vec2i ts = new Vec2i(60, 60), tg = new Vec2i(188, 140);
            var walls = ObstacleField(W, H, o.Seed ^ 0xBEEFUL, 0.22f, false, ts, tg, pf, path);
            Func<int, float> wallCost = i => walls[i];
            yield return PathCase(o, "path128.obstacles22pct", pf, path, ts, tg, wallCost, true, 20000, warm, iters);
            var weighted = ObstacleField(W, H, o.Seed ^ 0xBEEFUL, 0.22f, true, ts, tg, pf, path);
            Func<int, float> weightedCost = i => weighted[i];
            yield return PathCase(o, "path128.obstacles22pct.cost1to3", pf, path, ts, tg, weightedCost, true, 20000, warm, iters);

            // --- worst case: goal sealed inside a ring of walls -> search exhausts its budget
            var sealedMap = new float[W * H];
            for (int i = 0; i < sealedMap.Length; i++) sealedMap[i] = 1f;
            Vec2i goal = new Vec2i(200, 200);
            for (int y = goal.Y - 3; y <= goal.Y + 3; y++)
                for (int x = goal.X - 3; x <= goal.X + 3; x++)
                    if (Math.Abs(x - goal.X) == 3 || Math.Abs(y - goal.Y) == 3) sealedMap[y * W + x] = float.PositiveInfinity;
            Func<int, float> sealedCost = i => sealedMap[i];
            yield return PathCase(o, "unreachable.maxExp20000", pf, path, new Vec2i(20, 20), goal, sealedCost, true, 20000, o.Q(3, 8), o.Q(20, 60));
            yield return PathCase(o, "unreachable.unbounded256x256", pf, path, new Vec2i(20, 20), goal, sealedCost, true, W * H, o.Q(2, 4), o.Q(8, 25));
        }

        private static float[] ObstacleField(int w, int h, ulong seed, float wallChance, bool weighted, Vec2i ts, Vec2i tg, GridPathfinder pf, List<Vec2i> path)
        {
            var rng = new Rng(seed);
            var f = new float[w * h];
            for (int i = 0; i < f.Length; i++)
            {
                bool wall = rng.Chance(wallChance);
                float c = 1f + rng.NextFloat() * 2f;
                f[i] = wall ? float.PositiveInfinity : (weighted ? c : 1f);
            }
            f[ts.Y * w + ts.X] = 1f;
            f[tg.Y * w + tg.X] = 1f;
            if (!pf.FindPath(ts, tg, i => f[i], path, true, w * h)) // guarantee reachability: carve an L-shaped corridor
            {
                for (int x = ts.X; x <= tg.X; x++) f[ts.Y * w + x] = 1f;
                for (int y = ts.Y; y <= tg.Y; y++) f[y * w + tg.X] = 1f;
            }
            return f;
        }

        private static BenchResult PathCase(BenchOptions o, string name, GridPathfinder pf, List<Vec2i> path, Vec2i a, Vec2i b,
            Func<int, float> cost, bool eight, int maxExp, int warm, int iters)
        {
            bool found = false;
            var r = Timing.Measure(Category, "pathfinder." + name, "path", warm, iters, 1, () =>
            {
                found = pf.FindPath(a, b, cost, path, eight, maxExp);
            });
            r.Metric("found", found ? 1 : 0).Metric("path_tiles", path.Count).Metric("expansions", pf.LastExpansions)
             .Metric("distance_chebyshev", a.ChebyshevTo(b))
             .Metric("ns_per_expansion", pf.LastExpansions > 0 ? r.MedianNs / pf.LastExpansions : 0);
            return r;
        }

        // =========================================================================== entity store
        private sealed class BenchEntity : IEntity
        {
            public int Id { get; set; }
            public int Payload;
        }

        private static IEnumerable<BenchResult> EntityStoreOps(BenchOptions o)
        {
            const int N = 100_000;
            int iters = o.Q(8, 30), warm = o.Q(2, 5);
            var entities = new BenchEntity[N];
            for (int i = 0; i < N; i++) entities[i] = new BenchEntity { Id = i + 1, Payload = i };
            var rng = new Rng(o.Seed);
            var shuffled = new int[N];
            for (int i = 0; i < N; i++) shuffled[i] = i + 1;
            for (int i = N - 1; i > 0; i--) { int j = rng.Range(0, i + 1); int t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t; }

            EntityStore<BenchEntity> store = null;
            yield return Timing.Measure(Category, "entitystore.add100k(fresh store)", "add", warm, iters, N,
                () => { for (int i = 0; i < N; i++) store.Add(entities[i]); },
                () => store = new EntityStore<BenchEntity>());

            var full = new EntityStore<BenchEntity>();
            for (int i = 0; i < N; i++) full.Add(entities[i]);
            yield return Timing.Measure(Category, "entitystore.get100k(random hit)", "get", warm, iters, N, () =>
            {
                long s = 0;
                for (int i = 0; i < N; i++) s += full.Get(shuffled[i]).Payload;
                Timing.Sink += s;
            });
            yield return Timing.Measure(Category, "entitystore.tryget100k(miss)", "get", warm, iters, N, () =>
            {
                long s = 0;
                for (int i = 0; i < N; i++) if (full.TryGet(shuffled[i] + N, out var e)) s += e.Payload;
                Timing.Sink += s;
            });
            yield return Timing.Measure(Category, "entitystore.iterate100k(by index)", "item", warm, iters * 4, N, () =>
            {
                long s = 0;
                var items = full.Items;
                for (int i = 0; i < items.Count; i++) s += items[i].Payload;
                Timing.Sink += s;
            });
            yield return Timing.Measure(Category, "entitystore.remove100k(random order)", "remove", warm, iters, N,
                () => { for (int i = 0; i < N; i++) store.Remove(shuffled[i]); },
                () => { store = new EntityStore<BenchEntity>(); for (int i = 0; i < N; i++) store.Add(entities[i]); });
        }

        // =========================================================================== stockpile
        private static IEnumerable<BenchResult> StockpileOps(BenchOptions o)
        {
            int ops = o.Q(200_000, 1_000_000), iters = o.Q(10, 30), warm = 3;
            var sp = new Stockpile { Capacity = 1e9f };
            int nres = Counts.Resources;
            for (int r = 0; r < nres; r++) sp.SetRaw((ResourceType)r, 1e6f);
            var costs = new[]
            {
                new ResourceAmount { Type = ResourceType.Wood, Amount = 2f },
                new ResourceAmount { Type = ResourceType.Stone, Amount = 1f },
                new ResourceAmount { Type = ResourceType.Tools, Amount = 0.5f },
            };

            yield return Timing.Measure(Category, "stockpile.add+tryconsume", "pair", warm, iters, ops, () =>
            {
                for (int i = 0; i < ops; i++)
                {
                    var t = (ResourceType)(i % nres);
                    sp.Add(t, 1.5f);
                    sp.TryConsume(t, 1.5f);
                }
            });
            yield return Timing.Measure(Category, "stockpile.has", "op", warm, iters, ops, () =>
            {
                long s = 0;
                for (int i = 0; i < ops; i++) if (sp.Has((ResourceType)(i % nres), 10f)) s++;
                Timing.Sink += s;
            });
            yield return Timing.Measure(Category, "stockpile.trypay(3 costs)+refund", "op", warm, iters, ops, () =>
            {
                for (int i = 0; i < ops; i++)
                {
                    if (sp.TryPay(costs)) { sp.Add(ResourceType.Wood, 2f); sp.Add(ResourceType.Stone, 1f); sp.Add(ResourceType.Tools, 0.5f); }
                }
            });
            // RollStatistics with live flows every period (typical economy tick).
            var active = new Stockpile { Capacity = 1e9f };
            for (int r = 0; r < nres; r++) active.SetRaw((ResourceType)r, 1e6f);
            int rolls = ops / 8;
            yield return Timing.Measure(Category, "stockpile.rollstatistics(active flows)", "roll", warm, iters, rolls, () =>
            {
                for (int i = 0; i < rolls; i++)
                {
                    for (int r = 0; r < nres; r++) { active.Add((ResourceType)r, 1f); active.ConsumeUpTo((ResourceType)r, 0.5f); }
                    active.RollStatistics();
                }
            });
            // Idle resources: a flow happened once, then stopped. The EMA decays geometrically into the float
            // subnormal range and gets stuck at the smallest subnormal (x*0.1 rounds to 0), so every later roll
            // does subnormal arithmetic. This case measures that trap; compare with the fresh (all-zero) stockpile.
            var idle = new Stockpile { Capacity = 1e9f };
            for (int r = 0; r < nres; r++) { idle.Add((ResourceType)r, 5f); idle.ConsumeUpTo((ResourceType)r, 5f); }
            for (int i = 0; i < 2000; i++) idle.RollStatistics();
            double subnormal = idle.AvgIncome[0];
            var rIdle = Timing.Measure(Category, "stockpile.rollstatistics(idle, decayed EMA)", "roll", warm, iters, rolls, () =>
            {
                for (int i = 0; i < rolls; i++) idle.RollStatistics();
            });
            rIdle.Metric("avg_income_after_decay", subnormal).Metric("is_subnormal", subnormal != 0 && Math.Abs(subnormal) < 1.17549435e-38 ? 1 : 0);
            yield return rIdle;
            var fresh = new Stockpile();
            yield return Timing.Measure(Category, "stockpile.rollstatistics(fresh, zero EMA)", "roll", warm, iters, rolls, () =>
            {
                for (int i = 0; i < rolls; i++) fresh.RollStatistics();
            });
        }

        // =========================================================================== event bus
        private static long _evtCounter;
        private static void H1(PersonDiedEvent e) { _evtCounter += e.PersonId; }
        private static void H2(PersonDiedEvent e) { _evtCounter ^= e.SettlementId; }
        private static void H3(PersonDiedEvent e) { _evtCounter += (int)e.Cause; }
        private static void H4(PersonDiedEvent e) { if (e.AgeYears > 50f) _evtCounter++; }
        private static void H5(PersonDiedEvent e) { _evtCounter += e.CivilizationId; }

        private sealed class InstanceHandler
        {
            public long Count;
            public void OnBattle(BattleEvent e) { Count += e.AttackerLosses + e.DefenderLosses; }
        }

        private static IEnumerable<BenchResult> EventBusOps(BenchOptions o)
        {
            int ops = o.Q(500_000, 2_000_000), iters = o.Q(10, 30), warm = 3;
            var bus = new EventBus();

            yield return Timing.Measure(Category, "eventbus.publish(0 subscribers)", "publish", warm, iters, ops, () =>
            {
                for (int i = 0; i < ops; i++) bus.Publish(new YearPassedEvent { Year = i });
            });

            bus.Subscribe<PersonDiedEvent>(H1);
            bus.Subscribe<PersonDiedEvent>(H2);
            bus.Subscribe<PersonDiedEvent>(H3);
            bus.Subscribe<PersonDiedEvent>(H4);
            bus.Subscribe<PersonDiedEvent>(H5);
            yield return Timing.Measure(Category, "eventbus.publish(5 subscribers)", "publish", warm, iters, ops, () =>
            {
                for (int i = 0; i < ops; i++)
                    bus.Publish(new PersonDiedEvent { PersonId = i, SettlementId = i & 7, CivilizationId = 1, Cause = DeathCause.OldAge, AgeYears = i & 63 });
            });

            // Unsubscribe-to-empty path (list exists, zero handlers) and an instance-method subscriber with a large struct.
            var ih = new InstanceHandler();
            Action<BattleEvent> d = ih.OnBattle;
            bus.Subscribe(d);
            bus.Unsubscribe(d);
            yield return Timing.Measure(Category, "eventbus.publish(0 after unsubscribe)", "publish", warm, iters, ops, () =>
            {
                for (int i = 0; i < ops; i++) bus.Publish(new BattleEvent { AttackerLosses = i, DefenderLosses = 1 });
            });
            bus.Subscribe(d);
            yield return Timing.Measure(Category, "eventbus.publish(1 instance subscriber, 40B struct)", "publish", warm, iters, ops, () =>
            {
                for (int i = 0; i < ops; i++) bus.Publish(new BattleEvent { AttackerLosses = i, DefenderLosses = 1 });
            });
            Timing.Sink += _evtCounter + ih.Count;
        }

        // =========================================================================== rng
        private static IEnumerable<BenchResult> RngOps(BenchOptions o)
        {
            int ops = o.Q(2_000_000, 10_000_000), iters = o.Q(8, 25), warm = 3;
            var rng = new Rng(o.Seed);
            yield return WithThroughput(Timing.Measure(Category, "rng.nextuint", "call", warm, iters, ops, () =>
            { uint s = 0; for (int i = 0; i < ops; i++) s += rng.NextUInt(); Timing.Sink += s; }));
            yield return WithThroughput(Timing.Measure(Category, "rng.nextfloat", "call", warm, iters, ops, () =>
            { float s = 0; for (int i = 0; i < ops; i++) s += rng.NextFloat(); Timing.SinkD += s; }));
            yield return WithThroughput(Timing.Measure(Category, "rng.range(0,1000)", "call", warm, iters, ops, () =>
            { long s = 0; for (int i = 0; i < ops; i++) s += rng.Range(0, 1000); Timing.Sink += s; }));
            yield return WithThroughput(Timing.Measure(Category, "rng.chance(0.3)", "call", warm, iters, ops, () =>
            { long s = 0; for (int i = 0; i < ops; i++) if (rng.Chance(0.3f)) s++; Timing.Sink += s; }));
            yield return WithThroughput(Timing.Measure(Category, "rng.gaussian", "call", warm, iters, ops / 4, () =>
            { double s = 0; int n = ops / 4; for (int i = 0; i < n; i++) s += rng.Gaussian(); Timing.SinkD += s; }));
            yield return WithThroughput(Timing.Measure(Category, "rng.peryeartoperticks", "call", warm, iters, ops / 4, () =>
            { double s = 0; int n = ops / 4; for (int i = 0; i < n; i++) s += Rng.PerYearToPerTicks(0.02f, 1 + (i & 15)); Timing.SinkD += s; }));
        }

        private static BenchResult WithThroughput(BenchResult r)
        {
            if (r.MedianNs > 0) r.Metric("Mops_per_sec", 1e3 / r.MedianNs);
            return r;
        }

        // =========================================================================== world ops (people churn)
        private static IEnumerable<BenchResult> WorldOpsOps(BenchOptions o)
        {
            // Minimal hand-built world: one race, one civ, one settlement on a grassland map.
            WorldState state = null; WorldOps ops = null; Settlement settlement = null;
            var db = new GameDatabase();
            db.Register(new RaceDefinition { Id = RaceId.Human, Name = "Human", PluralName = "Humans", Adjective = "Human" });

            void Build(int residents)
            {
                var map = new WorldMap(64, 64);
                for (int i = 0; i < map.TileCount; i++) map.Biomes[i] = Biome.Grassland;
                state = new WorldState(o.Seed, map);
                ops = new WorldOps(state, db, new EventBus());
                var civ = ops.CreateCivilization(RaceId.Human, "Bench");
                settlement = ops.CreateSettlement(civ, new Vec2i(32, 32), "Benchton");
                for (int i = 0; i < residents; i++) ops.CreatePerson(settlement, RaceId.Human, (Sex)(i & 1), 20f + (i % 40));
            }

            int createN = o.Q(5000, 20000);
            yield return Timing.Measure(Category, "worldops.createperson", "person", 2, o.Q(5, 15), createN,
                () => { for (int i = 0; i < createN; i++) ops.CreatePerson(settlement, RaceId.Human, (Sex)(i & 1), 25f); },
                () => Build(0));

            foreach (int size in new[] { 100, 1000, 10000 })
            {
                int kills = Math.Min(size, 1000);
                var victims = new List<Person>(kills);
                var r = Timing.Measure(Category, "worldops.killperson+flush(settlement=" + size + ")", "death", 1, o.Q(5, 12), kills,
                    () =>
                    {
                        for (int i = 0; i < victims.Count; i++) ops.KillPerson(victims[i], DeathCause.OldAge);
                        ops.FlushRemovals();
                    },
                    () =>
                    {
                        Build(size);
                        victims.Clear();
                        var rng = new Rng(o.Seed + (ulong)size);
                        var items = state.People.Items;
                        // random victims (not the tail) so List<int>.Remove has to scan
                        var picked = new HashSet<int>();
                        while (victims.Count < kills)
                        {
                            int idx = rng.Range(0, items.Count);
                            if (picked.Add(idx)) victims.Add(items[idx]);
                        }
                    });
                r.Metric("settlement_residents", size);
                yield return r;
            }
        }
    }
}
