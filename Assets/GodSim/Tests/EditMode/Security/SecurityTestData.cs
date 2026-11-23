using GodSim.Core;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Security
{
    /// <summary>Shared fixtures for security tests: a database with a hand-registered god power set and a small world.</summary>
    internal static class SecurityTestData
    {
        public static readonly Vec2i TownAt = new Vec2i(20, 20);
        public const int OceanFromX = 56;

        /// <summary>Registers a known god power set, then fills every other slot with TestDb stubs and freezes.</summary>
        public static GameDatabase CreatePowerDb()
        {
            var db = new GameDatabase();
            db.Register(new GodPowerDefinition { Type = GodPowerType.Bless, Name = "Bless", Category = GodPowerCategory.Blessing, ManaCost = 5f, CooldownTicks = 0, MinRadius = 1, MaxRadius = 6 });
            db.Register(new GodPowerDefinition { Type = GodPowerType.Rain, Name = "Rain", Category = GodPowerCategory.Blessing, ManaCost = 10f, CooldownTicks = 5, MinRadius = 1, MaxRadius = 6, RequiresSettlementInRange = true });
            db.Register(new GodPowerDefinition { Type = GodPowerType.Lightning, Name = "Lightning", Category = GodPowerCategory.Wrath, ManaCost = 20f, CooldownTicks = 3, MinRadius = 0, MaxRadius = 3, RequiresLandTarget = true });
            db.Register(new GodPowerDefinition { Type = GodPowerType.SpawnTribe, Name = "Spawn Tribe", Category = GodPowerCategory.Creation, ManaCost = 50f, CooldownTicks = 50, MinRadius = 1, MaxRadius = 2, RequiresLandTarget = true, UsesRaceParameter = true });
            db.Register(new GodPowerDefinition { Type = GodPowerType.Meteor, Name = "Meteor", Category = GodPowerCategory.Wrath, ManaCost = 80f, CooldownTicks = 100, MinRadius = 1, MaxRadius = 4, MinWorldAge = AgeId.Iron });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        /// <summary>64x64 grassland with an ocean strip (x &gt;= 56), one Human settlement at (20,20) and plenty of mana.</summary>
        public static SimContext CreateWorld(GameDatabase db, out Settlement settlement, out Civilization civ)
        {
            var ctx = TestWorlds.CreateContext(db, 64);
            var map = ctx.State.Map;
            for (int y = 0; y < map.Height; y++)
                for (int x = OceanFromX; x < map.Width; x++)
                {
                    int i = map.Index(x, y);
                    map.Biomes[i] = Biome.DeepOcean;
                    map.Elevation[i] = -0.4f;
                }
            settlement = TestWorlds.AddSettlement(ctx, RaceId.Human, TownAt, 6, out civ);
            ctx.State.God.MaxMana = 500f;
            ctx.State.God.Mana = 400f;
            return ctx;
        }

        /// <summary>Basic-API replacement for StringAssert.Contains (Unity/NUnit portability).</summary>
        public static void AssertContains(string fragment, string text)
        {
            NUnit.Framework.Assert.IsTrue(text != null && text.Contains(fragment), "expected '" + fragment + "' in: " + (text ?? "<null>"));
        }

        public static GodCommand Cmd(GodPowerType power, int x, int y, int radius = 1, int parameter = 0, string source = "local")
        {
            return new GodCommand { Power = power, Target = new Vec2i(x, y), Radius = radius, Parameter = parameter, Source = source };
        }
    }
}
