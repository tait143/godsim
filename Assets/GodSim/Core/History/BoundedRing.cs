using System;
using System.Collections;
using System.Collections.Generic;

namespace GodSim.Core.History
{
    /// <summary>Fixed-capacity FIFO ring (oldest entries are overwritten). Index 0 is the oldest entry.</summary>
    internal sealed class BoundedRing<T> : IReadOnlyList<T>
    {
        private T[] _items;
        private int _head;   // index of the oldest element
        private int _count;

        public BoundedRing(int capacity)
        {
            Capacity = capacity < 1 ? 1 : capacity;
            _items = new T[Math.Min(Capacity, 256)];
        }

        public int Capacity { get; }
        public int Count => _count;
        /// <summary>Entries dropped because the ring was full.</summary>
        public long Evicted { get; private set; }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                return _items[(_head + index) % _items.Length];
            }
        }

        public void Add(T item)
        {
            if (_count < _items.Length)
            {
                _items[(_head + _count) % _items.Length] = item;
                _count++;
                return;
            }
            if (_items.Length < Capacity)
            {
                // Grow (only happens while _head == 0 because nothing has been evicted yet).
                var bigger = new T[Math.Min(Capacity, _items.Length * 2)];
                for (int i = 0; i < _count; i++) bigger[i] = _items[(_head + i) % _items.Length];
                _items = bigger;
                _head = 0;
                _items[_count++] = item;
                return;
            }
            _items[_head] = item;
            _head = (_head + 1) % _items.Length;
            Evicted++;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            _count = 0;
            Evicted = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++) yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
