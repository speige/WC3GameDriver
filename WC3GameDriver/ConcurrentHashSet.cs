using System.Collections;
using System.Collections.Concurrent;

namespace WC3GameDriver
{
    public sealed class ConcurrentHashSet<T> : IEnumerable<T>
    {
        private readonly ConcurrentDictionary<T, bool> _dictionary;

        public ConcurrentHashSet()
        {
            _dictionary = new();
        }

        public ConcurrentHashSet(IEqualityComparer<T> comparer)
        {
            _dictionary = new(comparer);
        }

        public bool Add(T v)
        {
            return _dictionary.TryAdd(v, true);
        }

        public bool Contains(T v)
        {
            return _dictionary.ContainsKey(v);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _dictionary.Keys.GetEnumerator();
        }

        public void UnionWith(IEnumerable<T> collection)
        {
            foreach (var v in collection)
            {
                Add(v);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
