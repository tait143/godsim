using GodSim.Core;
using GodSim.Core.Economy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Economy
{
    public class StorageSystemTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void SystemMetadata_MatchesContract()
        {
            var sys = new StorageSystem();
            Assert.AreEqual(12, sys.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(sys.Name));
        }

        [Test]
        public void Capacity_IsBasePlusStorageTimesCondition()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            var granary = EconServicesTestKit.Place(ctx, s, BuildingType.Granary);
            granary.Condition = 0.5f;

            EconServicesTestKit.TickOnce(new StorageSystem(), ctx);

            Assert.AreEqual(150f + 100f + 25f, s.Stockpile.Capacity, Eps);
        }

        [Test]
        public void Capacity_ScalesWithStorageModifier()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(30, 30), 4, out var civ);
            EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            civ.Modifiers.Storage = 2f;

            EconServicesTestKit.TickOnce(new StorageSystem(), ctx);

            Assert.AreEqual((150f + 100f) * 2f, s.Stockpile.Capacity, Eps);
        }

        [Test]
        public void Capacity_IgnoresIncompleteAndDestroyedBuildings()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse, complete: false);
            var destroyed = EconServicesTestKit.Place(ctx, s, BuildingType.Granary);
            ctx.Ops.DestroyBuilding(destroyed, DestroyCause.Fire);
            var flagged = EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            flagged.IsDestroyed = true; // still listed, but flagged (pending removal)

            ctx.DeltaTicks = 12;
            new StorageSystem().Tick(ctx);

            Assert.AreEqual(150f, s.Stockpile.Capacity, Eps);
        }

        [Test]
        public void Initialize_SetsCapacityImmediately()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            ctx.DeltaTicks = 12;
            new StorageSystem().Initialize(ctx);
            Assert.AreEqual(250f, s.Stockpile.Capacity, Eps);
        }

        [Test]
        public void Spoilage_DoesNotHappenAtOrBelowSixtyPercent()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            s.Stockpile.SetRaw(ResourceType.Food, 90f); // exactly 60% of 150

            EconServicesTestKit.TickOnce(new StorageSystem(), ctx);

            Assert.AreEqual(90f, s.Stockpile.Get(ResourceType.Food), Eps);
            Assert.AreEqual(0f, s.Mem(EconomyServiceKeys.FoodSpoiled), Eps);
        }

        [Test]
        public void Spoilage_AppliesToExcessAboveSixtyPercent_ScaledByDeltaTicks()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            var sys = new StorageSystem();

            s.Stockpile.SetRaw(ResourceType.Food, 150f); // excess over 90 = 60
            EconServicesTestKit.TickOnce(sys, ctx);
            Assert.AreEqual(150f - 60f * 0.0015f * 12f, s.Stockpile.Get(ResourceType.Food), Eps);

            s.Stockpile.SetRaw(ResourceType.Food, 150f);
            EconServicesTestKit.TickOnce(sys, ctx, 24);
            Assert.AreEqual(150f - 60f * 0.0015f * 24f, s.Stockpile.Get(ResourceType.Food), Eps);
            Assert.AreEqual(60f * 0.0015f * 24f, s.Mem(EconomyServiceKeys.FoodSpoiled), Eps);
        }

        [Test]
        public void Spoilage_OnlyAffectsFood()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            s.Stockpile.SetRaw(ResourceType.Wood, 150f);
            EconServicesTestKit.TickOnce(new StorageSystem(), ctx);
            Assert.AreEqual(150f, s.Stockpile.Get(ResourceType.Wood), Eps);
        }

        [Test]
        public void ComputeSpoilage_NeverExceedsExcess()
        {
            Assert.AreEqual(60f, StorageSystem.ComputeSpoilage(150f, 150f, 100000), Eps);
            Assert.AreEqual(0f, StorageSystem.ComputeSpoilage(50f, 150f, 12), Eps);
            Assert.AreEqual(0f, StorageSystem.ComputeSpoilage(150f, 150f, 0), Eps);
            Assert.AreEqual(0f, StorageSystem.ComputeSpoilage(float.NaN, 150f, 12), Eps);
        }

        [Test]
        public void Tick_ClampsToCapacityWhenStorageIsLost()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            var store = EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            var sys = new StorageSystem();
            EconServicesTestKit.TickOnce(sys, ctx);
            s.Stockpile.SetRaw(ResourceType.Stone, 240f);

            ctx.Ops.DestroyBuilding(store, DestroyCause.Fire);
            ctx.Ops.FlushRemovals();
            EconServicesTestKit.TickOnce(sys, ctx);

            Assert.AreEqual(150f, s.Stockpile.Capacity, Eps);
            Assert.AreEqual(150f, s.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(90f, s.Mem(EconomyServiceKeys.StorageOverflow), Eps);
        }

        [Test]
        public void Tick_SkipsDeadSettlements()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            s.IsAlive = false;
            s.Stockpile.Capacity = 999f;
            EconServicesTestKit.TickOnce(new StorageSystem(), ctx);
            Assert.AreEqual(999f, s.Stockpile.Capacity, Eps);
        }

        [Test]
        public void Tick_SanitizesBadModifiersAndConditions()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out var civ);
            var store = EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            store.Condition = float.NaN;
            civ.Modifiers.Storage = float.NaN;
            EconServicesTestKit.TickOnce(new StorageSystem(), ctx);
            Assert.IsTrue(EconServicesTestKit.IsFinite(s.Stockpile.Capacity));
            Assert.AreEqual(150f, s.Stockpile.Capacity, Eps);

            civ.Modifiers.Storage = -5f;
            EconServicesTestKit.TickOnce(new StorageSystem(), ctx);
            Assert.IsTrue(s.Stockpile.Capacity >= 1f);
        }
    }
}
