using Avalonia;
using Everywhere.Automation;
using Everywhere.Linux.Hyprland;

namespace Everywhere.Linux.Automation;

/// <summary>A Context-owned compositor output or window, including applications without accessibility support.</summary>
public sealed class HyprlandVisualElement : VisualElement
{
    private readonly HyprlandVisualElementBackend _backend;
    private readonly HyprlandWindow? _window;
    private readonly HyprlandMonitor? _monitor;

    private HyprlandVisualElement(VisualElementIdentity identity, HyprlandVisualElementBackend backend,
        HyprlandWindow? window, HyprlandMonitor? monitor) : base(identity, backend.AllocateId())
    {
        _backend = backend;
        _window = window;
        _monitor = monitor;
    }

    public static HyprlandVisualElement GetOrCreate(VisualElementRetention retention, HyprlandVisualElementBackend backend, HyprlandWindow window)
    {
        return retention.Context.GetIdentityMap<WindowIdentity>().GetOrAdd(retention,
            new WindowIdentity(backend.Connection.Instance, window.Address, window.ProcessId), (Backend: backend, Window: window),
            static (identity, state) => new HyprlandVisualElement(identity, state.Backend, state.Window, null));
    }

    public static HyprlandVisualElement GetOrCreate(VisualElementRetention retention, HyprlandVisualElementBackend backend, HyprlandMonitor monitor)
    {
        return retention.Context.GetIdentityMap<MonitorIdentity>().GetOrAdd(retention,
            new MonitorIdentity(backend.Connection.Instance, monitor.Name, monitor.Bounds), (Backend: backend, Monitor: monitor),
            static (identity, state) => new HyprlandVisualElement(identity, state.Backend, null, state.Monitor));
    }

    protected override VisualElementQueryResult QueryCore(VisualElementQueryRequest request)
    {
        var desktop = _backend.ReadDesktop();
        var window = _window is null ? null : desktop.Windows.FirstOrDefault(item => item.Address == _window.Address && item.ProcessId == _window.ProcessId);
        var monitor = _monitor is null ? null : desktop.Monitors.FirstOrDefault(item => item.Name == _monitor.Name && item.Bounds == _monitor.Bounds);
        if (window is null && monitor is null)
            throw new VisualElementProviderException(VisualElementQueryFailureKind.ElementUnavailable, "The compositor target disappeared or its output geometry changed.");
        var available = request.RequestedFields & ~VisualElementFields.Text;
        var windowMonitor = window is null ? null : desktop.Monitors.FirstOrDefault(item => item.Id == window.MonitorId);
        var isOffscreen = window is not null && (window.IsHidden || windowMonitor is null ||
            window.Workspace.Id != windowMonitor.ActiveWorkspace.Id &&
            (windowMonitor.SpecialWorkspace.Id == 0 || window.Workspace.Id != windowMonitor.SpecialWorkspace.Id));
        var snapshot = new VisualElementSnapshot(Id, window is null ? VisualElementType.Screen : VisualElementType.TopLevel,
            isOffscreen ? VisualElementStates.Offscreen : VisualElementStates.None,
            window?.Title ?? monitor?.Name, null, false, window?.Bounds ?? monitor?.Bounds,
            window?.ProcessId ?? 0, window?.Handle ?? 0);
        return new(this, snapshot, available, request.RequestedFields & ~available, null);
    }

    protected override IVisualElementCursor CreateEnumeratorCore(VisualElementRelation relation, VisualElementQueryRequest request,
        int offset, CancellationToken cancellationToken)
    {
        return new LinuxVisualCursor(Context, retention => Enumerate(retention, relation), request, offset, cancellationToken);
    }

    private IEnumerable<VisualElement> Enumerate(VisualElementRetention retention, VisualElementRelation relation)
    {
        var desktop = _backend.ReadDesktop();
        var currentWindow = _window is null ? null : desktop.Windows.FirstOrDefault(item => item.Address == _window.Address && item.ProcessId == _window.ProcessId);
        if (_window is not null && currentWindow is null)
            throw new VisualElementProviderException(VisualElementQueryFailureKind.ElementUnavailable, "The source window disappeared.");
        if (relation is VisualElementRelation.PreviousSibling or VisualElementRelation.NextSibling)
        {
            var step = relation == VisualElementRelation.PreviousSibling ? -1 : 1;
            if (_monitor is not null)
            {
                var items = desktop.Monitors.OrderBy(item => item.Id).ToArray();
                var position = Array.FindIndex(items, item => item.Name == _monitor.Name);
                for (var index = position + step; position >= 0 && index >= 0 && index < items.Length; index += step)
                    yield return GetOrCreate(retention, _backend, items[index]);
            }
            else if (currentWindow is not null)
            {
                var items = desktop.Windows.Where(item => item.IsMapped && item.MonitorId == currentWindow.MonitorId).ToArray();
                var position = Array.FindIndex(items, item => item.Address == currentWindow.Address);
                for (var index = position + step; position >= 0 && index >= 0 && index < items.Length; index += step)
                    yield return GetOrCreate(retention, _backend, items[index]);
            }
            yield break;
        }
        if (relation == VisualElementRelation.Parent && _window is not null)
        {
            var monitor = desktop.Monitors.FirstOrDefault(item => item.Id == currentWindow?.MonitorId);
            if (monitor is not null)
                yield return GetOrCreate(retention, _backend, monitor);
        }
        if (relation != VisualElementRelation.Child)
            yield break;
        if (_monitor is not null)
        {
            foreach (var window in desktop.Windows.Where(item => item.IsMapped && item.MonitorId == _monitor.Id))
                yield return GetOrCreate(retention, _backend, window);
        }
        else if (_window is not null)
        {
            nint pointer;
            try
            {
                pointer = _backend.Accessibility.Call(() => _backend.FindAccessibleWindow(_window));
            }
            catch (NotSupportedException) { yield break; }
            if (pointer == 0)
                yield break;
            try
            {
                yield return AtspiVisualElement.GetOrCreate(retention, _backend, _window, pointer);
            }
            finally { _backend.Accessibility.Release(pointer); }
        }
    }

    protected override Task<IVisualElementCapture> CaptureCoreAsync(CancellationToken cancellationToken)
    {
        return LinuxScreenCapture.CaptureAsync(QueryCore(new(VisualElementFields.Bounds, 0)).Snapshot.Bounds ?? default, cancellationToken);
    }

    protected override VisualElement AdoptCore(VisualElementRetention destinationRetention)
    {
        return _window is not null ? GetOrCreate(destinationRetention, _backend, _window) : GetOrCreate(destinationRetention, _backend, _monitor ?? throw new InvalidOperationException());
    }

    protected override void ReleaseCore()
    {
    }
    private sealed record WindowIdentity(string Instance, string Address, int ProcessId);
    private sealed record MonitorIdentity(string Instance, string Name, PixelRect Bounds);
}

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
