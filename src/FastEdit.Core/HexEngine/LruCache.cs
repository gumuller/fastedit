namespace FastEdit.Core.HexEngine;

public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _cache;
    private readonly LinkedList<(TKey Key, TValue Value)> _lruList;
    private readonly object _lock = new();

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _cache = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
        _lruList = new LinkedList<(TKey, TValue)>();
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Add(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                _lruList.Remove(existing);
                existing.Value = (key, value);
                _lruList.AddFirst(existing);
                return;
            }

            if (_cache.Count >= _capacity)
            {
                var lru = _lruList.Last!;
                _lruList.RemoveLast();
                _cache.Remove(lru.Value.Key);
            }

            var newNode = _lruList.AddFirst((key, value));
            _cache[key] = newNode;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
        }
    }
}
