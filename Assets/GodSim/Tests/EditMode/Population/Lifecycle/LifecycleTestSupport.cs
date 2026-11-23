using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Population;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Population.Lifecycle
{
    /// <summary>Test-only food producer: adds food to every living settlement each tick.</summary>
    internal sealed class TestFoodSource : ISimSystem
    {
        /// <summary>Flat food added per tick per settlement.</summary>
        public float FlatPerTick;
        /// <summary>Food added per tick per living adult resident (workers).</summary>
        public float PerAdultPerTick;
        /// <summary>Upper bound of food added per tick per settlement (bounded farmland).</summary>
        public float MaxPerTick = float.MaxValue;

        public string Name => "Test.FoodSource";
        public int TickInterval => 1;
        public void Initialize(SimContext ctx) { }

        public void Tick(SimContext ctx)
        {
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (!s.IsAlive) continue;
                int adults = 0;
                for (int r = 0; r < s.ResidentIds.Count; r++)
                {
                    var p = ctx.State.People.Get(s.ResidentIds[r]);
                    if (p != null && p.IsAlive && p.IsAdult) adults++;
                }
                float amount = Math.Min(MaxPerTick, FlatPerTick + PerAdultPerTick * adults) * ctx.DeltaTicks;
                s.Stockpile.Add(ResourceType.Food, amount);
            }
        }
    }

    internal static class LifecycleTestSupport
    {
        /// <summary>Stub database; <paramref name="tweak"/> may edit race definitions before the database is frozen.</summary>
        public static GameDatabase CreateDb(Action<GameDatabase> tweak = null)
        {
            var db = TestDb.CreateStub(false);
            tweak?.Invoke(db);
            db.Freeze();
            return db;
        }

        /// <summary>A civilization with one settlement and no people, with a big food stock.</summary>
        public static Settlement EmptySettlement(SimContext ctx, RaceId race, Vec2i at, float food, out Civilization civ)
        {
            var s = TestWorlds.AddSettlement(ctx, race, at, 0, out civ);
            s.Stockpile.Capacity = Math.Max(s.Stockpile.Capacity, food * 2f + 100f);
            s.Stockpile.SetRaw(ResourceType.Food, food);
            return s;
        }

        public static Person Add(SimContext ctx, Settlement s, RaceId race, Sex sex, float age, int motherId = -1, int fatherId = -1)
        {
            return ctx.Ops.CreatePerson(s, race, sex, age, motherId, fatherId);
        }

        public static void Marry(Person a, Person b)
        {
            a.SpouseId = b.Id;
            b.SpouseId = a.Id;
        }

        /// <summary>Creates <paramref name="n"/> married couples (wife, husband) of the settlement's race.</summary>
        public static List<Person> AddCouples(SimContext ctx, Settlement s, int n, float age = 25f)
        {
            var wives = new List<Person>();
            for (int i = 0; i < n; i++)
            {
                var w = Add(ctx, s, s.Race, Sex.Female, age);
                var h = Add(ctx, s, s.Race, Sex.Male, age + 1f);
                w.Traits = PersonTraits.None;
                h.Traits = PersonTraits.None;
                Marry(w, h);
                wives.Add(w);
            }
            return wives;
        }

        public static List<ISimSystem> AllSystems(TestFoodSource food)
        {
            var list = new List<ISimSystem>();
            if (food != null) list.Add(food);
            list.AddRange(LifecycleModule.CreateSystems());
            return list;
        }

        public static int CountAlive(SimContext ctx)
        {
            return ctx.State.LivingPopulation;
        }

        /// <summary>Deterministic fingerprint of the living population.</summary>
        public static ulong Fingerprint(WorldState state)
        {
            ulong h = 1469598103934665603UL;
            var items = state.People.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var p = items[i];
                if (!p.IsAlive) continue;
                h = Hashing.Combine(h, (ulong)p.Id);
                h = Hashing.Combine(h, (ulong)(long)Math.Round(p.AgeYears * 1000.0));
                h = Hashing.Combine(h, (ulong)(p.SpouseId + 1));
                h = Hashing.Combine(h, (ulong)p.ChildrenCount);
                h = Hashing.Combine(h, (ulong)(long)Math.Round(p.Satiation * 10000.0));
            }
            return h;
        }
    }
}
