using System.Collections;

namespace IntegrationTests.Infrastructure.Collections;

/// <summary>
/// Snapshot-enumerating list for observation logs written by EventBus consumers
/// while tests read/clear the same instance.
/// </summary>
public sealed class ThreadSafeList<T> : IReadOnlyList<T>
{
	private readonly List<T> _items = [];
	private readonly object _gate = new();

	public void Add(T item)
	{
		lock (_gate)
			_items.Add(item);
	}

	public void Clear()
	{
		lock (_gate)
			_items.Clear();
	}

	public int Count
	{
		get
		{
			lock (_gate)
				return _items.Count;
		}
	}

	public T this[int index]
	{
		get
		{
			lock (_gate)
				return _items[index];
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		List<T> copy;
		lock (_gate)
			copy = [.. _items];
		return copy.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
