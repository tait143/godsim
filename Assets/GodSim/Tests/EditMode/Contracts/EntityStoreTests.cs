using System;
using System.Collections.Generic;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class EntityStoreTests
    {
        private sealed class E : IEntity { public int Id { get; set; } public int Payload; }

        [Test]
        public void AddGetRemove_Basics()
        {
            var s = new EntityStore<E>();
            var a = new E { Id = 5 };
            var b = new E { Id = 9 };
            s.Add(a);
            s.Add(b);
            Assert.AreEqual(2, s.Count);
            Assert.AreSame(a, s.Get(5));
            Assert.AreSame(b, s[1]);
            Assert.IsTrue(s.Contains(9));
            Assert.IsTrue(s.TryGet(9, out var got));
            Assert.AreSame(b, got);
            Assert.IsFalse(s.TryGet(1, out got));
            Assert.IsNull(got);
            Assert.IsNull(s.Get(1));
            Assert.IsTrue(s.Remove(5));
            Assert.IsFalse(s.Remove(5));
            Assert.IsFalse(s.Contains(5));
            Assert.AreEqual(1, s.Count);
            Assert.AreSame(b, s[0]);
            Assert.AreSame(b, s.Get(9));
        }

        [Test]
        public void DuplicateId_Throws_AndLeavesStoreIntact()
        {
            var s = new EntityStore<E>();
            s.Add(new E { Id = 1, Payload = 1 });
            Assert.Throws<InvalidOperationException>(() => s.Add(new E { Id = 1, Payload = 2 }));
            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(1, s.Get(1).Payload);
        }

        [Test]
        public void AddNull_Throws()
        {
            var s = new EntityStore<E>();
            Assert.Throws<ArgumentNullException>(() => s.Add(null));
            Assert.AreEqual(0, s.Count);
        }

        [Test]
        public void RemoveLast_And_RemoveOnly()
        {
            var s = new EntityStore<E>();
            s.Add(new E { Id = 1 });
            s.Add(new E { Id = 2 });
            Assert.IsTrue(s.Remove(2));
            Assert.AreEqual(1, s.Get(1).Id);
            Assert.IsTrue(s.Remove(1));
            Assert.AreEqual(0, s.Count);
            s.Add(new E { Id = 1 });
            Assert.AreEqual(1, s.Count);
        }

        [Test]
        public void SwapRemove_IsDeterministicInsertionOrder()
        {
            var s = new EntityStore<E>();
            for (int i = 1; i <= 5; i++) s.Add(new E { Id = i });
            s.Remove(2); // last (5) moves into slot 1
            var ids = new List<int>();
            for (int i = 0; i < s.Count; i++) ids.Add(s[i].Id);
            CollectionAssert.AreEqual(new[] { 1, 5, 3, 4 }, ids);
        }

        [Test]
        public void Clear_EmptiesEverything()
        {
            var s = new EntityStore<E>();
            for (int i = 0; i < 10; i++) s.Add(new E { Id = i });
            s.Clear();
            Assert.AreEqual(0, s.Count);
            Assert.IsFalse(s.Contains(3));
            s.Add(new E { Id = 3 });
            Assert.AreEqual(1, s.Count);
        }

        [Test]
        public void RandomOps_10k_MatchReferenceDictionary()
        {
            var r = new Rng(2024);
            var s = new EntityStore<E>();
            var reference = new Dictionary<int, E>();
            for (int op = 0; op < 10000; op++)
            {
                int id = r.Range(-50, 400);
                int kind = r.Range(0, 10);
                if (kind < 5)
                {
                    var e = new E { Id = id, Payload = op };
                    if (reference.ContainsKey(id)) Assert.Throws<InvalidOperationException>(() => s.Add(e));
                    else { s.Add(e); reference[id] = e; }
                }
                else if (kind < 9)
                {
                    Assert.AreEqual(reference.Remove(id), s.Remove(id), "op " + op);
                }
                else
                {
                    reference.TryGetValue(id, out var expected);
                    Assert.AreSame(expected, s.Get(id));
                }

                if (op % 97 == 0 || op == 9999)
                {
                    Assert.AreEqual(reference.Count, s.Count);
                    var seen = new HashSet<int>();
                    for (int i = 0; i < s.Count; i++)
                    {
                        var item = s[i];
                        Assert.IsTrue(seen.Add(item.Id), "duplicate in Items");
                        Assert.AreSame(reference[item.Id], item);
                        Assert.AreSame(item, s.Get(item.Id), "index map stale for id " + item.Id);
                        Assert.AreSame(item, s.Items[i]);
                    }
                    foreach (var kv in reference) Assert.IsTrue(s.Contains(kv.Key));
                }
            }
        }

        [Test]
        public void IdAllocator_IsMonotonicAndPerKind()
        {
            var ids = new IdAllocator();
            Assert.AreEqual(1, ids.Person());
            Assert.AreEqual(2, ids.Person());
            Assert.AreEqual(1, ids.Building());
            Assert.AreEqual(1, ids.Settlement());
            Assert.AreEqual(1, ids.Civilization());
            Assert.AreEqual(1, ids.Army());
            Assert.AreEqual(1, ids.War());
            Assert.AreEqual(3, ids.NextPersonId);
        }
    }
}
