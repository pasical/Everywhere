namespace Everywhere.Linux.Desktop;

/// <summary>The explicitly selected desktop integration, independent of Avalonia's renderer.</summary>
public enum LinuxDesktopKind
{
    Hyprland,
    X11,
    PlasmaWayland,
}