using System.Collections;
using System.Collections.Generic;

namespace GodSim.Core.History
{
    /// <summary>Read-only live view over a list (callers cannot cast it back and mutate the source).</summary>
    internal sealed class ReadOnlyListView<T> : IReadOnlyList<T>
    {
        private readonly List<T> _source;
        public ReadOnlyListView(List<T> source) { _source = source; }
        public int Count => _source.Count;
        public T this[int index] => _source[index];
        public IEnumerator<T> GetEnumerator() => _source.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _source.GetEnumerator();
    }
}
