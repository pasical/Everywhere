using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Everywhere.Interop;
using Everywhere.Views;

namespace Everywhere.Linux.Platform;

/// <summary>Configures owned UI surfaces through the compositor bridge and backend input regions.</summary>
public sealed partial class LinuxWindowHelper : IWindowHelper, IDisposable
{
    private nint _display;

    /// <summary>Flushes owned surface hides before an interactive capture or element preview.</summary>
    public Task WaitForHiddenSurfacesAsync(CancellationToken cancellationToken)
    {
        return AvaloniaLocator.Current.GetRequiredService<IWindowingPlatform>() is HyprlandWindowingPlatform platform
            ? platform.WaitForHiddenSurfacesAsync(cancellationToken)
            : Task.CompletedTask;
    }


    public void SetWindowProperties(Window window, WindowInputMode? inputMode = null, bool? hitTestVisible = null, WindowLayer? layer = null)
    {
        // Some transient surfaces configure their input policy in their constructor.
        // Apply the same owned-window role before their first Wayland map.
        InitializeWindow(window);
        if (inputMode is { } mode)
            window.ShowActivated = mode == WindowInputMode.Normal;
        if (hitTestVisible is { } isHitTestVisible)
            window.IsHitTestVisible = isHitTestVisible;
        if (layer is { } band)
            window.Topmost = band != WindowLayer.Normal;
        if (window.PlatformImpl?.TryGetFeature<HyprlandWindowState>() is { } state)
        {
            if (inputMode is { } policy)
                state.CanActivate = policy == WindowInputMode.Normal;
            if (hitTestVisible is { } canHitTest)
                state.IsHitTestVisible = canHitTest;
            if (layer is >= WindowLayer.Overlay && !state.CanActivate && !state.IsHitTestVisible &&
                double.IsFinite(window.Width) && double.IsFinite(window.Height))
                state.SetOverlayBounds(new PixelRect(window.Position, new PixelSize((int)Math.Round(window.Width * window.DesktopScaling),
                    (int)Math.Round(window.Height * window.DesktopScaling))), false);
            state.RefreshInputPolicy();
        }
    }

    /// <summary>Places one process-owned overlay without changing saved compositor configuration.</summary>
    public void PrepareOverlay(Window window, PixelRect bounds, bool isInteractive)
    {
        var state = window.PlatformImpl?.TryGetFeature<HyprlandWindowState>() ?? throw new InvalidOperationException("The Hyprland windowing bridge is not initialized.");
        state.SetOverlayBounds(bounds, isInteractive);
    }

    public bool TryPrepareInput(Window window) => false;
    public IWindowInputCapture? TryCaptureInput(Window window) => null;
    public void RaiseWindow(Window window)
    {
        if (window.PlatformImpl?.TryGetFeature<HyprlandWindowState>() is { } state)
        {
            state.Raise();
            return;
        }
        if (TryGetX11Window(window, out var handle))
        {
            XRaiseWindow(_display, handle);
            XFlush(_display);
        }
    }
    public bool GetEffectiveVisible(Window window) => window.IsVisible && window.WindowState != WindowState.Minimized;
    public bool? IsRegionCovered(Window window, PixelRect bounds) => null;
    public void SetCloaked(Window window, bool cloaked)
    {
        if (cloaked)
            window.Hide();
        else
            window.Show();
    }
    public bool AnyModelDialogOpened(Window window) => window.OwnedWindows.Any(item => item.IsVisible);
    public void RequestUserAttention(Window window)
    {
        if (!TryGetX11Window(window, out var handle))
            return;
        var hints = XGetWMHints(_display, handle);
        if (hints == 0)
            return;
        try
        {
            Marshal.WriteInt64(hints, Marshal.ReadInt64(hints) | 256);
            XSetWMHints(_display, handle, hints);
            XFlush(_display);
        }
        finally { XFree(hints); }
    }
    public bool BringToForeground(Window window)
    {
        if (window.PlatformImpl?.TryGetFeature<HyprlandWindowState>() is { } state)
            state.Activate();
        else
            window.Activate();
        return window.IsActive;
    }
    public double SetCornerRadius(Window window, double radius) => 0;
    public void InitializeWindow(Window window)
    {
        if (window.PlatformImpl?.TryGetFeature<HyprlandWindowState>() is not { } state)
            return;
        state.IsAnchoredPopup = window is TextSelectionToolbarWindow;
        if (window is ChatWindow or TextSelectionToolbarWindow)
            state.IsFloating = true;
        else if (window is VisualElementOverlayWindow)
        {
            state.IsFloating = true;
            state.CanActivate = false;
            state.IsHitTestVisible = false;
        }
    }
    public void Dispose()
    {
        var display = Interlocked.Exchange(ref _display, 0);
        if (display != 0)
            XCloseDisplay(display);
    }

    private bool TryGetX11Window(Window window, out nint handle)
    {
        handle = 0;
        if (window.TryGetPlatformHandle() is not { Handle: not 0, HandleDescriptor: "XID" } platformHandle)
            return false;
        if (_display == 0)
            _display = XOpenDisplay(0);
        if (_display == 0)
            return false;
        handle = platformHandle.Handle;
        return true;
    }

    [LibraryImport("libX11.so.6")]
    private static partial nint XOpenDisplay(nint name);
    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(nint display);
    [LibraryImport("libX11.so.6")]
    private static partial int XRaiseWindow(nint display, nint window);
    [LibraryImport("libX11.so.6")]
    private static partial int XFlush(nint display);
    [LibraryImport("libX11.so.6")]
    private static partial nint XGetWMHints(nint display, nint window);
    [LibraryImport("libX11.so.6")]
    private static partial int XSetWMHints(nint display, nint window, nint hints);
    [LibraryImport("libX11.so.6")]
    private static partial int XFree(nint pointer);
}
