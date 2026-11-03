namespace PptSlider;

/// <summary>A stack that keeps only its newest <see cref="Capacity"/> items: pushing past it drops the oldest (undo history).</summary>
public sealed class BoundedStack<T>(int capacity)
{
    readonly LinkedList<T> items = new();

    public int Capacity { get; } = capacity;
    public int Count => items.Count;

    public void Push(T item)
    {
        items.AddLast(item);
        if (items.Count > Capacity) items.RemoveFirst();
    }

    public bool TryPop(out T item)
    {
        if (items.Last is not { } last) { item = default!; return false; }
        item = last.Value;
        items.RemoveLast();
        return true;
    }

    public void Clear() => items.Clear();
}
