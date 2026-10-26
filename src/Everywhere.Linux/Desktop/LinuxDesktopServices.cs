using Everywhere.Interop;
using Everywhere.Linux.Hyprland;
using Everywhere.Linux.Platform;
using Everywhere.ProcessIsolation.Hosting;

namespace Everywhere.Linux.Desktop;

/// <summary>
/// Selects and composes one Linux desktop backend per process. Common automation and
/// selection code never selects a compositor implicitly. New desktops provide these
/// services here, together with their native windowing and input integrations.
/// </summary>
public sealed class LinuxDesktopServices
{
    public ICompositorStateProvider Compositor { get; }
    public IOverlayWindowController Overlays { get; }
    public ISilentScreenCapturer ScreenCapturer { get; }

    private LinuxDesktopServices(ICompositorStateProvider compositor, IOverlayWindowController overlays, ISilentScreenCapturer screenCapturer)
    {
        Compositor = compositor;
        Overlays = overlays;
        ScreenCapturer = screenCapturer;
    }

    /// <summary>Selects the supported backend for the explicitly inherited session.</summary>
    public static LinuxDesktopServices Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            throw new NotSupportedException("This Linux build currently requires a Hyprland session. Other Wayland backends are not implemented.");
        var compositor = new HyprlandCompositorStateProvider(new HyprlandConnection());
        return new(compositor, new HyprlandOverlayWindowController(), new HyprlandSilentScreenCapturer(compositor));
    }

    /// <summary>Installs the native UI bridge before creating any Avalonia windows.</summary>
    public void InstallWindowingPlatform() => HyprlandWindowingPlatform.Install();

    /// <summary>Creates the compositor-specific global input observer behind its shared contract.</summary>
    public IOverlayInputWatcher CreateOverlayInputWatcher() => new HyprlandOverlayInputWatcher();

    /// <summary>Creates the compositor-specific shortcut Host behind its shared contract.</summary>
    public static IProcessRoleSession CreateInputHostSession() => new HyprlandInputHostSession();
}