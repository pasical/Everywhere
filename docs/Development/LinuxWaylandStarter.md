# Ordinary Wayland desktop starter

Plasma and GNOME Wayland start through Avalonia.Wayland's ordinary toplevel and text-input
implementation. It does not install `HyprlandWindowingPlatform`, issue compositor
commands, assign guessed global coordinates or reuse Hyprland's input bindings.
`StandardWaylandWindowController` permits ordinary floating-application intent
while explicitly rejecting overlay roles and unsupported global window policies.

`UnavailableWaylandStateProvider`, `UnavailableWaylandScreenCapturer` and
`UnavailableWaylandSelectionService` mark global observation, silent capture and
interactive picking as unported. The selection service rejects requests before
creating a surface. Shortcuts, text-selection assistance and input recording are
also unported; Main uses a local unavailable shortcut listener instead of an RPC
proxy that would repeatedly restart an unsupported Input Host.

This is a starting point for desktop contributors, not feature parity with
Hyprland. Future desktop work should replace the unavailable capabilities at the
composition root without moving desktop policies into shared Core or changing
Windows/macOS behavior. Ordinary input uses the framework's native protocols;
no separate input-method implementation or dependency is introduced here.

## Plasma validation

The retained [Plasma result](LinuxHyprlandEvidence/plasma-wayland-starter.json)
records ordinary Main and Chat rendered by `Avalonia.Wayland.WaylandTopLevelFactory`,
both Hosts authenticated without recovery loops, and Fcitx5 Pinyin candidates plus
Chinese commit events in the real ChatWindow. The 72 Linux tests passed.

KWin's nested X11 keyboard may have an empty device name. Its default empty
`KWIN_DEVICE_SKIPS_INPUT_METHOD` entry can therefore bypass the input method for
both GTK and Everywhere. The lab uses a nonmatching sentinel before starting KWin;
production does not change this variable or the user's input-method configuration.
This was confirmed against [KWin's input filter](https://github.com/KDE/kwin/blob/Plasma/6.7/src/input.cpp).


## GNOME validation

The retained [GNOME result](LinuxHyprlandEvidence/gnome-wayland-starter.json)
records Mutter 50.5 with an explicit 1280x800 virtual output, native Avalonia
Wayland Main/Chat, two healthy authenticated Hosts, and IBus libpinyin preedit,
candidates and Chinese commit events in the actual ChatWindow. The 75 Linux tests
passed. These are minimal session tests, not full distribution or multi-monitor acceptance.

The lab supplies private auxiliary D-Bus, PipeWire and WirePlumber instances.
WirePlumber starts after the private PipeWire socket is ready, so its session
manager does not exit before the media server accepts connections.
Mutter Devkit alone initially has no output, so the lab creates one explicitly.
Its native monitor stream and virtual input are used only to observe/control the
owned test desktop. Those privileged test APIs are not product screenshot,
input or global-state implementations and do not change the user's desktop.
