# Disposable desktop validation

These scripts run an owned minimal desktop in a read-only Arch rootfs with a
private home, D-Bus, X server, Wayland socket and media services. They never mount
the user's display, session bus or profile. Only the lab state directory is
writable inside the sandbox; application and probe binaries are read-only.
Network access is shared, so use only public pages and disposable accounts.

## Preparation

On a Linux host with Bubblewrap and unprivileged user namespaces, prepare
`LAB/root.x86_64` from an official Arch bootstrap. Install packages into that
rootfs, never into the host. The tested package snapshot was
`https://archive.archlinux.org/repos/2026/10/01/`; newer repositories may no longer
carry Plasma's X11 session. This lab used KWin 6.7.5 and Mutter 50.5.

Required packages include Python, python-gobject, python-pillow, dbus, mesa,
xorg-server-xvfb, xdotool, kwin, kwin-x11, plasma-workspace,
plasma-x11-session, gnome-shell, mutter, mutter-devkit, gsettings-desktop-schemas,
fcitx5, fcitx5-chinese-addons, fcitx5-gtk, fcitx5-qt, ibus, ibus-libpinyin,
pipewire, wireplumber, gst-plugin-pipewire, gst-plugins-base, gst-plugins-good,
ffmpeg and CJK fonts. Repository package splits can change; retain the tested
snapshot when reproducing these results. Package installation needs root inside
a private mount namespace; desktop sessions themselves must run as an ordinary
user. The rootfs must contain the standard `/mnt`, `/opt` and `/srv` directories.

Build the Linux application and `Everywhere.Linux.Probes` in Release for
`net10.0/linux-x64`, using the project's normal cloud-build configuration.
`BUILD` below is that checkout, containing the build outputs, not the installed
user application.

## Start and control

```sh
python3 launch.py LAB BUILD plasma-x11
python3 launch.py LAB BUILD plasma-wayland
python3 launch.py LAB BUILD gnome-wayland
python3 control.py --background main LAB/gnome-wayland -- /srv/Everywhere.Linux.Probes --desktop-main /mnt/input.jsonl --ui
python3 control.py LAB/gnome-wayland -- /srv/Everywhere.Linux.Probes --desktop-main /mnt/activation.jsonl
python3 control.py --stop LAB/gnome-wayland
```

The first probe command runs the actual application Main window and records
text-input events from its actual windows. The second command activates Chat
through the application's existing single-instance path. Host child processes
use the production entry point. No production test hooks are installed.

State and logs live under `LAB/DESKTOP`. Inspect `ready.json`, `main.log`,
`compositor.log`, `native-stream.log` and `input.jsonl`. The control socket is
mode 0600 and accepts commands only inside the owned sandbox. Keep options before
the state directory because the remaining arguments are passed to the command.

For GNOME, `gnome_input.py` accepts a JSON sequence of pointer, button and evdev
key actions through the control command. It uses only the owned Mutter test
session. GNOME capture comes from that session's native monitor via PipeWire;
other sessions are captured from their private Xvfb parent. These lab APIs do
not constitute product global-input or screenshot support.

## Live view and recordings

```sh
python3 viewer.py LAB
```

Open `http://127.0.0.1:8864/` on that host, or forward that loopback port through
SSH. The page is read-only and shows the live or last captured frame for each
session. Recordings remain in each state directory: `recording.mkv` for Plasma,
`gnome-native-recording.mkv` for GNOME. Stop a session before archiving its video.
The recorded successful runs were retained as `x11-ime-recording.mkv`,
`plasma-wayland-ime-recording.mkv` and `gnome-wayland-ime-recording.mkv` in the
validation lab. Rootfs and videos are runtime artifacts, not source files.

See [X11 starter](../../../../docs/Development/LinuxX11Starter.md) and
[Wayland starter](../../../../docs/Development/LinuxWaylandStarter.md) for the
accepted capabilities and explicit limits. These are minimal desktop sessions,
not complete distribution, real-login, multi-monitor or overlay acceptance.
