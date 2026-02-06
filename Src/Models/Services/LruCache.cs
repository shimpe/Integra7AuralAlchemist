using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map;
    private readonly LinkedList<(TKey key, TValue value)> _list;

    public LruCache(int capacity)
    {
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>();
        _list = new LinkedList<(TKey, TValue)>();
    }

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _list.Remove(existing);
        }
        else if (_map.Count >= _capacity)
        {
            var last = _list.Last!;
            _map.Remove(last.Value.key);
            _list.RemoveLast();
        }

        var node = new LinkedListNode<(TKey, TValue)>((key, value));
        _list.AddFirst(node);
        _map[key] = node;
    }
}
