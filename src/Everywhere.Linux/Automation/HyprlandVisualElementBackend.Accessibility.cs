using System.Diagnostics;
using Avalonia;
using Everywhere.Automation;
using Everywhere.Linux.Hyprland;

namespace Everywhere.Linux.Automation;

partial class HyprlandVisualElementBackend
{
    /// <summary>Consumes the owned window proxy and retains only the confirmed native target.</summary>
    private VisualElementQueryResult QueryAccessible(VisualElementRetention retention, HyprlandWindow window, nint root,
        VisualElementLocator locator, PixelPoint point, VisualElementQueryRequest request)
    {
        return Accessibility.Call(() =>
        {
            var selected = root;
            try
            {
                // Chromium enables its renderer tree when an assistive client
                // requests attributes. Reading names alone leaves web content lazy.
                var attributes = AtspiNative.Checked(AtspiNative.atspi_accessible_get_attributes(root, out var attributesError), attributesError);
                if (attributes != 0)
                    AtspiNative.g_hash_table_unref(attributes);
                if (locator.Kind is VisualElementLocatorKind.Point or VisualElementLocatorKind.Pointer)
                {
                    // Window-relative coordinates avoid unsupported global AT-SPI coordinates on native Wayland.
                    var local = point - window.Bounds.Position;
                    var deadline = Stopwatch.StartNew();
                    var visited = new HashSet<nint> { root };
                    for (var depth = 0; depth < MaximumHitTestDepth && deadline.ElapsedMilliseconds < QueryBudgetMilliseconds; depth++)
                    {
                        var component = AtspiNative.atspi_accessible_get_component_iface(selected);
                        if (component == 0)
                            break;
                        nint child;
                        try
                        {
                            child = AtspiNative.Checked(AtspiNative.atspi_component_get_accessible_at_point(component, local.X, local.Y, AtspiCoordinates.Window, out var error), error);
                        }
                        finally { AtspiNative.g_object_unref(component); }
                        if (child == selected)
                        {
                            AtspiNative.g_object_unref(child);
                            child = 0;
                        }
                        // Some providers implement this operation as Contains(self).
                        // Inspect bounded children rather than stopping at the window.
                        if (child == 0)
                            child = FindChildAtPoint(selected, new PixelPoint(local.X, local.Y), window, deadline);
                        if (child == 0)
                            break;
                        if (!visited.Add(child))
                        {
                            AtspiNative.g_object_unref(child);
                            break;
                        }
                        if (selected != root)
                            AtspiNative.g_object_unref(selected);
                        selected = child;
                    }
                }
                else if (locator.Kind == VisualElementLocatorKind.Focused)
                {
                    selected = FindFocusedElement(root);
                }
                return AtspiVisualElement.GetOrCreate(retention, this, window, selected).Query(request);
            }
            finally
            {
                if (selected != root)
                    AtspiNative.g_object_unref(selected);
                AtspiNative.g_object_unref(root);
            }
        });
    }

    private nint FindChildAtPoint(nint parent, PixelPoint point, HyprlandWindow window, Stopwatch deadline)
    {
        if (IsBoundsQueryBackedOff(window))
            return 0;
        var count = Math.Min(AtspiNative.Count(parent), MaximumApplicationWindows);
        for (var index = count - 1; index >= 0 && deadline.ElapsedMilliseconds < QueryBudgetMilliseconds; index--)
        {
            var child = AtspiNative.Child(parent, index);
            if (child == 0)
                continue;
            try
            {
                if (AtspiNative.IsDetachedEmbeddedRoot(child)) continue;
                if (AtspiNative.ReadExtents(child, AtspiCoordinates.Window) is { } bounds && bounds.Contains(point))
                {
                    var result = child;
                    child = 0;
                    return result;
                }
            }
            catch (TimeoutException)
            {
                RecordBoundsTimeout(window);
                return 0;
            }
            catch (VisualElementProviderException) { }
            finally { if (child != 0) AtspiNative.g_object_unref(child); }
        }
        return 0;
    }

    private static nint FindFocusedElement(nint root)
    {
        var pending = new Queue<nint>();
        pending.Enqueue(AtspiNative.g_object_ref(root));
        var deadline = Stopwatch.StartNew();
        try
        {
            for (var count = 0; pending.Count > 0 && count < MaximumSearchedControls && deadline.ElapsedMilliseconds < QueryBudgetMilliseconds; count++)
            {
                var item = pending.Dequeue();
                try
                {
                    var states = AtspiNative.atspi_accessible_get_state_set(item);
                    if (states != 0)
                    {
                        try
                        {
                            if (AtspiNative.atspi_state_set_contains(states, AtspiState.Focused) != 0)
                                return item == root ? root : AtspiNative.g_object_ref(item);
                        }
                        finally { AtspiNative.g_object_unref(states); }
                    }
                    var childCount = Math.Min(AtspiNative.Count(item), MaximumSearchedControls - count - pending.Count);
                    for (var index = 0; index < childCount; index++)
                    {
                        var child = AtspiNative.Child(item, index);
                        if (child != 0)
                            pending.Enqueue(child);
                    }
                }
                finally { AtspiNative.g_object_unref(item); }
            }
            return root;
        }
        finally
        {
            while (pending.TryDequeue(out var item))
                AtspiNative.g_object_unref(item);
        }
    }

    /// <summary>Returns an owned AT-SPI window only when PID and window identity are unambiguous.</summary>
    public nint FindAccessibleWindow(HyprlandWindow window)
    {
        nint desktop;
        try
        {
            desktop = Accessibility.Call(() => AtspiNative.atspi_get_desktop(0));
        }
        catch (NotSupportedException) { return 0; }
        if (desktop == 0)
            return 0;
        try
        {
            var applications = Math.Min(AtspiNative.Count(desktop), MaximumSearchedControls);
            for (var index = 0; index < applications; index++)
            {
                var application = AtspiNative.Child(desktop, index);
                if (application == 0)
                    continue;
                try
                {
                    if (AtspiNative.Pid(application) != window.ProcessId)
                        continue;
                    var count = AtspiNative.Count(application);
                    if (count > MaximumApplicationWindows)
                        return 0;
                    nint candidate = 0;
                    var matches = 0;
                    try
                    {
                        for (var childIndex = 0; childIndex < count; childIndex++)
                        {
                            var child = AtspiNative.Child(application, childIndex);
                            if (child == 0)
                                continue;
                            var name = AtspiNative.Name(child);
                            if (count == 1 || name == window.Title && !string.IsNullOrEmpty(window.Title))
                            {
                                matches++;
                                if (candidate != 0)
                                    AtspiNative.g_object_unref(candidate);
                                candidate = child;
                            }
                            else
                                AtspiNative.g_object_unref(child);
                        }
                        if (matches != 1)
                            return 0;
                        var result = candidate;
                        candidate = 0;
                        return result;
                    }
                    finally { if (candidate != 0) AtspiNative.g_object_unref(candidate); }
                }
                finally { AtspiNative.g_object_unref(application); }
            }
            return 0;
        }
        finally { AtspiNative.g_object_unref(desktop); }
    }

}
