using System;
using System.Collections;
using System.Collections.Generic;

namespace GodSim.Core.History
{
    /// <summary>
    /// Bounded, ordered sample series that never loses its timeline. Samples are stored at a uniform stride
    /// (in raw-sample units). When the buffer is full, every other sample is dropped (2:1) and the stride
    /// doubles, so a long game keeps evenly spaced samples from the very first one to the latest one.
    /// The last stored element is always the most recent sample (a "provisional" tail that is replaced until
    /// the next stride boundary), and the first element is always the first sample ever recorded.
    /// Memory is bounded by <see cref="Capacity"/>; the backing array grows by doubling up to it.
    /// </summary>
    internal sealed class DownsamplingBuffer<T> : IReadOnlyList<T> where T : struct, IHistorySample<T>
    {
        private const int InitialSize = 64;

        private T[] _items;
        private long[] _seqs;
        private int _count;
        private long _nextSeq;
        private long _stride = 1;
        private bool _hasCarry;
        private T _carry;

        public DownsamplingBuffer(int capacity)
        {
            Capacity = capacity < 4 ? 4 : capacity;
            int initial = Math.Min(InitialSize, Capacity);
            _items = new T[initial];
            _seqs = new long[initial];
        }

        /// <summary>Maximum number of stored samples.</summary>
        public int Capacity { get; }
        /// <summary>Number of stored samples (&lt;= Capacity).</summary>
        public int Count => _count;
        /// <summary>Raw samples represented by each stored sample (1, 2, 4, ...).</summary>
        public long Stride => _stride;
        /// <summary>Total samples ever added.</summary>
        public long TotalAdded => _nextSeq;
        /// <summary>How many 2:1 downsampling passes have happened.</summary>
        public int Downsamplings { get; private set; }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                return _items[index];
            }
        }

        /// <summary>The most recent sample (default if empty).</summary>
        public T Last => _count > 0 ? _items[_count - 1] : default(T);

        public void Add(T sample)
        {
            long seq = _nextSeq++;
            if (_hasCarry) { sample = sample.AbsorbEarlier(_carry); _hasCarry = false; _carry = default(T); }

            // Provisional tail (between stride boundaries): replace it, folding its additive data in.
            if (_count > 0 && _seqs[_count - 1] % _stride != 0)
            {
                _items[_count - 1] = sample.AbsorbEarlier(_items[_count - 1]);
                _seqs[_count - 1] = seq;
                return;
            }

            if (_count == Capacity)
            {
                Compact();
                if (_hasCarry) { sample = sample.AbsorbEarlier(_carry); _hasCarry = false; _carry = default(T); }
            }
            else if (_count == _items.Length)
            {
                int size = Math.Min(Capacity, _items.Length * 2);
                Array.Resize(ref _items, size);
                Array.Resize(ref _seqs, size);
            }
            _items[_count] = sample;
            _seqs[_count] = seq;
            _count++;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _count);
            _count = 0;
            _nextSeq = 0;
            _stride = 1;
            _hasCarry = false;
            _carry = default(T);
            Downsamplings = 0;
        }

        /// <summary>Index of point <paramref name="i"/> when picking <paramref name="points"/> evenly spaced items out of <paramref name="count"/> (first and last always included).</summary>
        public static int ResampleIndex(int i, int count, int points)
        {
            if (points <= 1 || count <= 1) return count - 1;
            // Integer math keeps this exact and platform independent.
            return (int)((long)i * (count - 1) / (points - 1));
        }

        private void Compact()
        {
            long newStride = _stride * 2;
            int w = 0;
            bool hasCarry = false;
            T carry = default(T);
            for (int r = 0; r < _count; r++)
            {
                T item = _items[r];
                if (hasCarry) { item = item.AbsorbEarlier(carry); hasCarry = false; }
                if (_seqs[r] % newStride == 0)
                {
                    _items[w] = item;
                    _seqs[w] = _seqs[r];
                    w++;
                }
                else { carry = item; hasCarry = true; }
            }
            Array.Clear(_items, w, _count - w);
            _count = w;
            _stride = newStride;
            _hasCarry = hasCarry;
            _carry = carry;
            Downsamplings++;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++) yield return _items[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
