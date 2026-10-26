# X11 desktop starter

This restores core designs from `fork/dev-linux` through the current Linux desktop
contracts, targeting a minimal Plasma/KWin X11 session. It does not restore the
old branch's complete facade, shortcut hooks, selection listener or toolbar.

`X11CompositorStateProvider` reads EWMH stacking, foreground, desktop visibility,
PID, client geometry and optional frame extents. `X11SilentScreenCapturer` reads
visible root pixels. Short-lived XCB connections own every reply and native error;
foreign-window races do not install a global Xlib error handler in Avalonia.
`X11WindowController` uses Avalonia's ordinary X11 windows, WM_HINTS and input
shapes. The shared AT-SPI and picker/capture pipeline remain available.

The first target is one X screen at scale 1 and a standard little-endian 32-bit
TrueColor pixel layout. RandR output enumeration, mixed DPI, global shortcuts,
shortcut recording and text-selection assistance remain unported. They must not
be presented as successful registrations. Main uses an explicit unavailable
shortcut listener, while the authenticated Input Host remains healthy and idle.

An explicit X11 session takes precedence over inherited Wayland/Hyprland markers.
Main selects Avalonia X11 directly rather than falling back from Wayland.

The retained desktop probes run actual Main with test-only committed-input
observation (`--desktop-main REPORT --ui`). `--desktop-state REPORT` exercises
state and screenshot contracts without initializing a UI renderer. Use disposable
profiles only. The desktop lab uses a read-only rootfs, separate namespaces, user
home, runtime and D-Bus session. Its loopback viewer is read-only; it never captures
the host's desktop. GNOME/Plasma Wayland support is separate follow-up work.
