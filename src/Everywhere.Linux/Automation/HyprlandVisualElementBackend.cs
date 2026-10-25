using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Everywhere.Automation;
using Everywhere.Interop;
using Everywhere.Linux.Hyprland;
using Everywhere.ProcessIsolation.Automation;

namespace Everywhere.Linux.Automation;

/// <summary>Composes compositor topology with scoped application accessibility without querying through Main's overlays.</summary>
public sealed partial class HyprlandVisualElementBackend : IVisualElementBackend, IVisualPickerResolver
{
    public HyprlandConnection Connection { get; } = new();
    public AtspiClient Accessibility { get; } = new();
    private const int MaximumApplicationWindows = 128;
    private const int BoundsRetryDelayMilliseconds = 1000;
    private long _nextId;
    private readonly Dictionary<(int ProcessId, string Address), long> _boundsRetryTimes = [];

    /// <summary>Reports the short circuit for a window whose native bounds provider timed out.</summary>
    public bool IsBoundsQueryBackedOff(HyprlandWindow window)
    {
        var key = (window.ProcessId, window.Address);
        if (!_boundsRetryTimes.TryGetValue(key, out var retryAt))
            return false;
        if (Environment.TickCount64 < retryAt)
            return true;
        _boundsRetryTimes.Remove(key);
        return false;
    }

    /// <summary>Prevents one embedded provider from stalling every node in a tree read.</summary>
    public void RecordBoundsTimeout(HyprlandWindow window)
    {
        var now = Environment.TickCount64;
        foreach (var pair in _boundsRetryTimes.ToArray())
        {
            if (pair.Value <= now)
                _boundsRetryTimes.Remove(pair.Key);
        }
        _boundsRetryTimes[(window.ProcessId, window.Address)] = now + BoundsRetryDelayMilliseconds;
    }


    public HyprlandDesktop ReadDesktop()
    {
        return new(HyprlandDesktop.VisibleOutputs(Connection.Read<HyprlandMonitor[]>("monitors")), Connection.Read<HyprlandWindow[]>("clients"));
    }

    public string AllocateId() => "linux:" + Interlocked.Increment(ref _nextId);

    public VisualElementQueryResult? Query(VisualElementRetention retention, VisualElementLocator locator,
        VisualElementResolution resolution = VisualElementResolution.Direct, VisualElementQueryRequest? request = null)
    {
        return Acquire(retention, locator, resolution, request ?? VisualElementQueryRequest.Default, 0);
    }

    public VisualElementQueryResult? Resolve(IVisualElementBackend backend, VisualElementRetention retention, PixelPoint point,
        ScreenSelectionMode mode, int excludedProcessId, VisualElementQueryRequest request)
    {
        return Acquire(retention, VisualElementLocator.FromPoint(point), mode switch
        {
            ScreenSelectionMode.Screen => VisualElementResolution.Screen,
            ScreenSelectionMode.Window => VisualElementResolution.TopLevel,
            _ => VisualElementResolution.Direct,
        }, request, excludedProcessId);
    }

    public HyprlandVisualElement GetWindow(VisualElementRetention retention, HyprlandWindow window)
    {
        return HyprlandVisualElement.GetOrCreate(retention, this, window);
    }

    public void Dispose() => Accessibility.Dispose();

    private VisualElementQueryResult? Acquire(VisualElementRetention retention, VisualElementLocator locator,
        VisualElementResolution resolution, VisualElementQueryRequest request, int excludedProcessId)
    {
        var desktop = ReadDesktop();
        var point = locator.Kind is VisualElementLocatorKind.Point ? locator.Point : ReadPointer();
        if (resolution == VisualElementResolution.Screen || locator.Kind == VisualElementLocatorKind.Default && resolution == VisualElementResolution.Direct)
        {
            var monitor = desktop.Monitors.FirstOrDefault(item => item.Bounds.Contains(point)) ?? desktop.Monitors.FirstOrDefault();
            return monitor is null ? null : HyprlandVisualElement.GetOrCreate(retention, this, monitor).Query(request);
        }
        HyprlandWindow? window;
        if (locator.Kind == VisualElementLocatorKind.NativeWindow)
            window = desktop.Windows.FirstOrDefault(item => item.Handle == locator.NativeWindowHandle && item.ProcessId != excludedProcessId);
        else if (locator.Kind is VisualElementLocatorKind.Focused or VisualElementLocatorKind.Default)
        {
            var active = Connection.Read<HyprlandWindow>("activewindow");
            window = desktop.Windows.FirstOrDefault(item => item.Address == active.Address && item.ProcessId != excludedProcessId);
        }
        else
            window = desktop.WindowAt(point, excludedProcessId);
        if (window is null)
            return null;
        if (resolution == VisualElementResolution.TopLevel)
            return GetWindow(retention, window).Query(request);
        nint root;
        try
        {
            root = Accessibility.Call(() => FindAccessibleWindow(window));
        }
        catch (NotSupportedException) { return GetWindow(retention, window).Query(request); }
        if (root == 0)
            return GetWindow(retention, window).Query(request);
        try { return QueryAccessible(retention, window, root, locator, point, request); }
        catch (VisualElementProviderException exception) when (exception.Kind == VisualElementQueryFailureKind.ElementUnavailable)
        {
            // Enabling a lazy bridge can replace its initial window proxy. The
            // previous Call has returned, so the native dispatcher can drain its
            // events before one cache refresh/reacquisition. Never retry indefinitely.
            root = Accessibility.Call(() => FindAccessibleWindow(window, shouldRefresh: true));
            return root == 0 ? GetWindow(retention, window).Query(request) :
                QueryAccessible(retention, window, root, locator, point, request);
        }
    }

    private PixelPoint ReadPointer()
    {
        var pointer = Connection.Read<Dictionary<string, int>>("cursorpos");
        return new PixelPoint(pointer["x"], pointer["y"]);
    }
}
