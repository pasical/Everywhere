using Everywhere.Automation;

namespace Everywhere.Linux.Automation;

/// <summary>Owns a lazy relation and all native elements exposed through it.</summary>
public sealed class LinuxVisualCursor : IVisualElementCursor
{
    public int Count => -1;
    public int Index { get; private set; } = -1;
    public VisualElementQueryResult Current => _current ?? throw new InvalidOperationException("The relation has not advanced.");
    object System.Collections.IEnumerator.Current => Current;
    private readonly VisualElementRetention _retention;
    private readonly IEnumerator<VisualElement> _items;
    private readonly VisualElementQueryRequest _request;
    private readonly CancellationToken _cancellationToken;
    private int _offset;
    private VisualElementQueryResult? _current;

    public LinuxVisualCursor(VisualContext context, Func<VisualElementRetention, IEnumerable<VisualElement>> items, VisualElementQueryRequest request, int offset, CancellationToken cancellationToken)
    {
        _retention = context.CreateRetention();
        _items = items(_retention).GetEnumerator();
        _request = request;
        _offset = offset;
        _cancellationToken = cancellationToken;
    }

    public bool MoveNext()
    {
        do
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!_items.MoveNext())
                return false;
            Index++;
        } while (_offset-- > 0);
        _current = _items.Current.Query(_request);
        return true;
    }

    public void Reset() => throw new NotSupportedException();
    public void Dispose()
    {
        _items.Dispose();
        _retention.Dispose();
    }
}