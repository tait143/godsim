using System;
using System.Collections.Generic;

namespace GodSim.Core
{
    public interface IEntity
    {
        int Id { get; set; }
    }

    /// <summary>
    /// Id-indexed store with deterministic iteration order (insertion order; removal = swap-remove).
    /// Do not remove entities directly while iterating: use <see cref="WorldOps"/> which defers removal
    /// until the engine flushes after each system. Dead/destroyed entities stay in the store (flagged)
    /// until the flush, so always check IsAlive / IsDestroyed while iterating.
    /// </summary>
    public sealed class EntityStore<T> where T : class, IEntity
    {
        private readonly List<T> _items = new List<T>();
        private readonly Dictionary<int, int> _indexById = new Dictionary<int, int>();

        public int Count => _items.Count;
        public T this[int position] => _items[position];
        public IReadOnlyList<T> Items => _items;

        public void Add(T item)
        {
            Guard.NotNull(item, nameof(item));
            if (_indexById.ContainsKey(item.Id)) throw new InvalidOperationException(typeof(T).Name + " id already exists: " + item.Id);
            _indexById[item.Id] = _items.Count;
            _items.Add(item);
        }

        public bool Remove(int id)
        {
            if (!_indexById.TryGetValue(id, out int idx)) return false;
            int last = _items.Count - 1;
            if (idx != last)
            {
                T moved = _items[last];
                _items[idx] = moved;
                _indexById[moved.Id] = idx;
            }
            _items.RemoveAt(last);
            _indexById.Remove(id);
            return true;
        }

        public bool Contains(int id) => _indexById.ContainsKey(id);

        public T Get(int id) => _indexById.TryGetValue(id, out int idx) ? _items[idx] : null;

        public bool TryGet(int id, out T item)
        {
            if (_indexById.TryGetValue(id, out int idx)) { item = _items[idx]; return true; }
            item = null;
            return false;
        }

        public void Clear() { _items.Clear(); _indexById.Clear(); }
    }

    /// <summary>Monotonic id allocation; ids are never reused within a world (safe references in saves/events).</summary>
    public sealed class IdAllocator
    {
        public int NextPersonId = 1;
        public int NextBuildingId = 1;
        public int NextSettlementId = 1;
        public int NextCivilizationId = 1;
        public int NextArmyId = 1;
        public int NextWarId = 1;

        public int Person() => NextPersonId++;
        public int Building() => NextBuildingId++;
        public int Settlement() => NextSettlementId++;
        public int Civilization() => NextCivilizationId++;
        public int Army() => NextArmyId++;
        public int War() => NextWarId++;
    }
}
