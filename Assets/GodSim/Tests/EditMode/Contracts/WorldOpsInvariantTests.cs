using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    /// <summary>
    /// Randomized model test of WorldOps: thousands of random (but contract-respecting) operations, with global
    /// referential invariants asserted after every flush.
    /// </summary>
    public class WorldOpsInvariantTests
    {
        private sealed class Fuzzer
        {
            public readonly SimContext Ctx;
            public readonly WorldOps Ops;
            public readonly WorldState S;
            public readonly Rng R;
            public readonly Dictionary<string, int> OpCounts = new Dictionary<string, int>();
            private readonly BuildingType[] _types = { BuildingType.House, BuildingType.House, BuildingType.House, BuildingType.Farm, BuildingType.Barracks, BuildingType.Well, BuildingType.Colossus };

            public Fuzzer(ulong seed, bool allowCrossCivMoves)
            {
                Ctx = TestWorlds.CreateContext(WorldInvariants.CreateDb(), 64, seed);
                Ops = Ctx.Ops;
                S = Ctx.State;
                R = new Rng(seed * 7919UL + 13UL);
                _crossCiv = allowCrossCivMoves;
                // A lake so that water handling (territory, placement) is exercised.
                for (int y = 28; y < 36; y++) for (int x = 28; x < 36; x++) S.Map.Biomes[S.Map.Index(x, y)] = Biome.Lake;
            }

            private readonly bool _crossCiv;

            private void Count(string op) { OpCounts[op] = OpCounts.TryGetValue(op, out int n) ? n + 1 : 1; }

            private T PickAlive<T>(IReadOnlyList<T> items, System.Func<T, bool> ok) where T : class
            {
                int n = items.Count;
                if (n == 0) return null;
                int start = R.Range(0, n);
                for (int k = 0; k < n; k++) { var it = items[(start + k) % n]; if (ok(it)) return it; }
                return null;
            }

            private Civilization AliveCiv() => PickAlive(S.Civilizations.Items, c => c.IsAlive);
            private Settlement AliveSettlement() => PickAlive(S.Settlements.Items, x => x.IsAlive);
            private Person AlivePerson() => PickAlive(S.People.Items, p => p.IsAlive);
            private Building LiveBuilding() => PickAlive(S.Buildings.Items, b => !b.IsDestroyed);

            private Vec2i Near(Vec2i c, int r) => S.Map.ClampToBounds(new Vec2i(c.X + R.RangeInclusive(-r, r), c.Y + R.RangeInclusive(-r, r)));

            public void Step()
            {
                int k = R.Range(0, 100);
                if (k < 3 || S.Civilizations.Count == 0)
                {
                    if (S.Civilizations.Count < 8) { Ops.CreateCivilization((RaceId)R.Range(0, Counts.Races)); Count("createCiv"); }
                    return;
                }
                if (k < 8)
                {
                    var civ = AliveCiv();
                    if (civ != null && S.Settlements.Count < 14)
                    {
                        var at = new Vec2i(R.Range(2, 62), R.Range(2, 62));
                        if (R.Chance(0.5f)) Ops.FoundSettlement(civ, at); else Ops.CreateSettlement(civ, at, null, -1, R.RangeInclusive(1, 8));
                        Count("foundSettlement");
                    }
                    return;
                }
                if (k < 25)
                {
                    var st = AliveSettlement();
                    if (st != null && S.People.Count < 400)
                    {
                        var mother = R.Chance(0.3f) ? PickAlive(S.People.Items, p => p.IsAlive && p.SettlementId == st.Id) : null;
                        Ops.CreatePerson(st, st.Race, (Sex)R.Range(0, 2), R.Range(0f, 80f), mother != null ? mother.Id : -1, -1, R.Chance(0.5f));
                        Count("createPerson");
                    }
                    return;
                }
                if (k < 37)
                {
                    var st = AliveSettlement();
                    if (st != null)
                    {
                        var b = Ops.CreateBuilding(st, _types[R.Range(0, _types.Length)], Near(st.Center, 9), (byte)R.Range(0, 8), R.Chance(0.6f));
                        Count(b != null ? "createBuilding" : "createBuildingBlocked");
                    }
                    return;
                }
                if (k < 40)
                {
                    var b = PickAlive(S.Buildings.Items, x => !x.IsDestroyed && !x.IsComplete);
                    if (b != null) { Ops.CompleteBuilding(b); Count("complete"); }
                    return;
                }
                if (k < 52)
                {
                    var p = AlivePerson();
                    if (p != null)
                    {
                        var home = PickAlive(S.Buildings.Items, b => !b.IsDestroyed && b.SettlementId == p.SettlementId && b.HousingCapacity > 0);
                        if (home != null) { bool ok = Ops.AssignHome(p, home); Count(ok ? "assignHome" : "assignHomeRejected"); }
                    }
                    return;
                }
                if (k < 62)
                {
                    var p = AlivePerson();
                    if (p != null)
                    {
                        if (R.Chance(0.2f)) { Ops.AssignJob(p, null, JobType.Builder); Count("assignBuilder"); }
                        else if (R.Chance(0.1f)) { Ops.ClearJob(p); Count("clearJob"); }
                        else
                        {
                            var w = PickAlive(S.Buildings.Items, b => !b.IsDestroyed && b.SettlementId == p.SettlementId && (b.Type == BuildingType.Farm || b.Type == BuildingType.Barracks));
                            if (w != null) { bool ok = Ops.AssignJob(p, w, w.Type == BuildingType.Farm ? JobType.Farmer : JobType.Soldier); Count(ok ? "assignJob" : "assignJobRejected"); }
                        }
                    }
                    return;
                }
                if (k < 70)
                {
                    var p = AlivePerson();
                    if (p != null) { Ops.KillPerson(p, (DeathCause)R.Range(0, 8)); Count("kill"); }
                    return;
                }
                if (k < 74)
                {
                    var b = LiveBuilding();
                    if (b != null) { Ops.DestroyBuilding(b, (DestroyCause)R.Range(0, 7)); Count("destroyBuilding"); }
                    return;
                }
                if (k < 76)
                {
                    var st = AliveSettlement();
                    if (st != null) { Ops.DestroySettlement(st, (DestroyCause)R.Range(0, 7), R.Chance(0.3f)); Count("destroySettlement"); }
                    return;
                }
                if (k < 78)
                {
                    var st = AliveSettlement();
                    var civ = PickAlive(S.Civilizations.Items, c => c.IsAlive && st != null && c.Id != st.CivilizationId && c.SettlementIds.Count > 0);
                    if (st != null && civ != null) { Ops.TransferSettlement(st, civ); Count("transfer"); }
                    return;
                }
                if (k < 84)
                {
                    var p = AlivePerson();
                    var to = p == null ? null : PickAlive(S.Settlements.Items, x => x.IsAlive && x.Id != p.SettlementId && (_crossCiv || x.CivilizationId == p.CivilizationId));
                    if (to != null) { Ops.MovePersonToSettlement(p, to); Count("move"); }
                    return;
                }
                if (k < 88)
                {
                    var a = PickAlive(S.People.Items, p => p.IsAlive && p.SpouseId < 0 && p.IsAdult);
                    var b = a == null ? null : PickAlive(S.People.Items, p => p.IsAlive && p.SpouseId < 0 && p.IsAdult && p.Id != a.Id && p.Sex != a.Sex);
                    if (a != null && b != null) { a.SpouseId = b.Id; b.SpouseId = a.Id; Count("marry"); } // population module links spouses directly
                    return;
                }
                if (k < 90)
                {
                    var civ = AliveCiv();
                    var leader = civ == null ? null : PickAlive(S.People.Items, p => p.IsAlive && p.CivilizationId == civ.Id);
                    if (leader != null) { civ.LeaderId = leader.Id; Count("setLeader"); }
                    return;
                }
                if (k < 93)
                {
                    var home = AliveSettlement();
                    var civ = home == null ? null : S.Civilizations.Get(home.CivilizationId);
                    if (civ != null && civ.IsAlive)
                    {
                        var soldiers = new List<Person>();
                        var items = S.People.Items;
                        for (int i = 0; i < items.Count && soldiers.Count < 6; i++)
                            if (items[i].IsAlive && items[i].SettlementId == home.Id && R.Chance(0.5f)) soldiers.Add(items[i]);
                        Ops.CreateArmy(civ, home, soldiers);
                        Count("createArmy");
                    }
                    return;
                }
                if (k < 95)
                {
                    var a = PickAlive(S.Diplomacy.Armies.Items, x => x.IsAlive);
                    if (a != null) { Ops.DisbandArmy(a); Count("disband"); }
                    return;
                }
                if (k < 96)
                {
                    // Collapse only civs that hold no settlements (collapsing a civ that still has settlements is a separate, ignored bug test).
                    var civ = PickAlive(S.Civilizations.Items, c => c.IsAlive && c.SettlementIds.Count == 0);
                    if (civ != null) { Ops.CollapseCivilization(civ, -1); Count("collapse"); }
                    return;
                }
                var cs = AliveSettlement();
                if (cs != null) { Ops.ClaimTerritory(cs, R.RangeInclusive(1, 4)); Count("claim"); }
            }
        }

        private static void Run(ulong seed, int ops, bool crossCiv, bool extended)
        {
            var f = new Fuzzer(seed, crossCiv);
            int flushes = 0;
            for (int i = 0; i < ops; i++)
            {
                f.Step();
                if (f.R.Chance(0.12f) || i == ops - 1)
                {
                    f.Ops.FlushRemovals();
                    flushes++;
                    var v = WorldInvariants.CheckCore(f.S, f.Ctx.Db);
                    if (extended) v.AddRange(WorldInvariants.CheckExtended(f.S));
                    if (v.Count > 0)
                        Assert.Fail("seed " + seed + " op " + i + ": " + v.Count + " invariant violation(s):\n" + string.Join("\n", v.GetRange(0, System.Math.Min(15, v.Count))));
                }
            }
            Assert.Greater(flushes, 50);
            // Sanity: the fuzzer really exercised the interesting operations.
            foreach (var op in new[] { "createPerson", "createBuilding", "assignHome", "assignJob", "kill", "destroyBuilding", "destroySettlement", "move", "createArmy" })
                Assert.IsTrue(f.OpCounts.ContainsKey(op), "fuzzer never ran " + op + " (seed " + seed + ")");
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        [TestCase(20261119UL)]
        public void RandomOperations_PreserveCoreInvariants(ulong seed)
        {
            Run(seed, 2000, false, false);
        }

        [TestCase(11UL)]
        [TestCase(12UL)]
        public void RandomOperations_CrossCivMigration_PreserveCoreInvariants(ulong seed)
        {
            Run(seed, 2000, true, false);
        }

        [Test]
        [Ignore("BUG: extended invariants fail under random ops (categories observed: [leader], [army-civ], [army-home]). [leader]: TransferSettlement/MovePersonToSettlement leave civ.LeaderId pointing at a person now in another civ. [army-civ]: cross-civ MovePersonToSettlement keeps ArmyId, so an army holds foreign soldiers. [army-home]: DestroySettlement/TransferSettlement leave armies whose HomeSettlementId is removed or now foreign. See targeted ignored tests in WorldOpsTests.")]
        public void RandomOperations_PreserveExtendedInvariants()
        {
            Run(1UL, 2000, true, true);
            Run(2UL, 2000, true, true);
        }

        [Test]
        public void RandomOperations_AreDeterministic()
        {
            var a = new Fuzzer(77, true);
            var b = new Fuzzer(77, true);
            for (int i = 0; i < 1500; i++) { a.Step(); b.Step(); if (i % 10 == 0) { a.Ops.FlushRemovals(); b.Ops.FlushRemovals(); } }
            a.Ops.FlushRemovals(); b.Ops.FlushRemovals();
            Assert.AreEqual(a.S.People.Count, b.S.People.Count);
            Assert.AreEqual(a.S.Buildings.Count, b.S.Buildings.Count);
            Assert.AreEqual(a.S.Rng.State, b.S.Rng.State);
            for (int i = 0; i < a.S.People.Count; i++)
            {
                Assert.AreEqual(a.S.People[i].Id, b.S.People[i].Id);
                Assert.AreEqual(a.S.People[i].Name, b.S.People[i].Name);
                Assert.AreEqual(a.S.People[i].HomeId, b.S.People[i].HomeId);
            }
            for (int i = 0; i < a.S.Map.TileCount; i++) Assert.AreEqual(a.S.Map.Owner[i], b.S.Map.Owner[i]);
        }
    }
}
