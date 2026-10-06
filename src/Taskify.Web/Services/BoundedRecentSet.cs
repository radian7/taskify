namespace Taskify.Web.Services;

/// <summary>
/// A set that remembers the most recent keys and forgets the oldest once it is full (a bounded LRU). The realtime
/// service uses it to ignore signals it has already handled (a redelivered event must not cause a second re-fetch).
/// </summary>
/// <typeparam name="T">The key type.</typeparam>
public sealed class BoundedRecentSet<T>
    where T : notnull
{
    private readonly int capacity;
    private readonly Dictionary<T, LinkedListNode<T>> nodes = [];
    private readonly LinkedList<T> order = new();
    private readonly object gate = new();

    /// <summary>Creates a set.</summary>
    /// <param name="capacity">The most keys remembered; must be positive.</param>
    public BoundedRecentSet(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.capacity = capacity;
    }

    /// <summary>Gets the number of keys remembered.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return nodes.Count;
            }
        }
    }

    /// <summary>Remembers a key, or refreshes it when it is already known.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the key was new; <see langword="false"/> when it was seen before.</returns>
    public bool TryAdd(T key)
    {
        lock (gate)
        {
            if (nodes.TryGetValue(key, out var existing))
            {
                order.Remove(existing);
                order.AddFirst(existing);
                return false;
            }

            nodes[key] = order.AddFirst(key);
            if (nodes.Count > capacity)
            {
                var oldest = order.Last!;
                order.RemoveLast();
                nodes.Remove(oldest.Value);
            }

            return true;
        }
    }
}
