"""Run a minimal desktop inside the lab's disposable rootfs and private D-Bus session.

The launcher supplies /mnt, /opt and /srv. No host user profile, display socket,
or session bus is mounted. The control socket is local to the lab and mode 0600.
"""
import json
import os
import signal
import socket
import subprocess
import sys
import time
from pathlib import Path

WORK = Path('/mnt')
MODE = sys.argv[1]
children = []


def spawn(arguments, name, environment=None):
    log = (WORK / (name + '.log')).open('ab')
    process = subprocess.Popen(arguments, env=os.environ | (environment or {}), stdout=log, stderr=subprocess.STDOUT)
    log.close()
    children.append(process)
    return process


def wait_for_path(path, process):
    for _ in range(200):
        if process.poll() is not None:
            raise RuntimeError('Desktop process exited: ' + str(process.args))
        if path.exists():
            return
        time.sleep(.05)
    raise TimeoutError(str(path))


# GNOME's session startup uses Gio.DBus.system even for a devkit display.
# Provide a private auxiliary bus, never the host's system bus or its services.
system_address = 'unix:path=' + str(WORK / 'runtime/system-bus')
(WORK / 'runtime/system-bus').unlink(missing_ok=True)
system_bus = spawn(['dbus-daemon', '--session', '--nofork', '--address=' + system_address], 'system-bus')
wait_for_path(WORK / 'runtime/system-bus', system_bus)
os.environ['DBUS_SYSTEM_BUS_ADDRESS'] = system_address
subprocess.run(['dbus-update-activation-environment', 'DBUS_SYSTEM_BUS_ADDRESS'], check=True)
# Mutter Devkit transports its nested output over a private PipeWire connection.
media = spawn(['pipewire'], 'pipewire')
wait_for_path(Path(os.environ['XDG_RUNTIME_DIR']) / 'pipewire-0', media)
spawn(['wireplumber'], 'wireplumber')

os.environ.update(DISPLAY=':90', LIBGL_ALWAYS_SOFTWARE='1', QT_QUICK_BACKEND='software')
xserver = spawn(['Xvfb', ':90', '-screen', '0', '1280x800x24', '-ac', '-nolisten', 'tcp'], 'xserver')
wait_for_path(Path('/tmp/.X11-unix/X90'), xserver)
if MODE == 'plasma-x11':
    os.environ.update(XDG_SESSION_TYPE='x11', XDG_CURRENT_DESKTOP='KDE', KDE_SESSION_VERSION='6',
                      QT_QPA_PLATFORM='xcb', GTK_IM_MODULE='fcitx', QT_IM_MODULE='fcitx', XMODIFIERS='@im=fcitx')
    subprocess.run(['dbus-update-activation-environment', 'DISPLAY', 'XDG_SESSION_TYPE', 'XDG_CURRENT_DESKTOP'], check=True)
    desktop = spawn(['kwin_x11', '--replace'], 'compositor')
    time.sleep(1)
    spawn(['plasmashell'], 'shell', {'QT_IM_MODULE': ''})
    spawn(['fcitx5', '--replace'], 'ime')
elif MODE == 'plasma-wayland':
    # KWin's nested X11 keyboard can have an empty device name. Its default
    # empty skip-list entry would bypass the input method for that device.
    # A nonmatching sentinel is confined to this lab, never production startup.
    os.environ.update(XDG_SESSION_TYPE='wayland', XDG_CURRENT_DESKTOP='KDE', KDE_SESSION_VERSION='6',
                      KWIN_DEVICE_SKIPS_INPUT_METHOD='everywhere-lab-no-device')
    desktop = spawn(['kwin_wayland', '--x11-display', ':90', '--width', '1280', '--height', '800',
                     '--socket', 'wayland-test', '--inputmethod', '/usr/bin/fcitx5'], 'compositor')
    wait_for_path(Path(os.environ['XDG_RUNTIME_DIR']) / 'wayland-test', desktop)
    os.environ.update(WAYLAND_DISPLAY='wayland-test', QT_QPA_PLATFORM='wayland', GDK_BACKEND='wayland')
    subprocess.run(['dbus-update-activation-environment', 'DISPLAY', 'WAYLAND_DISPLAY', 'XDG_SESSION_TYPE', 'XDG_CURRENT_DESKTOP'], check=True)
    spawn(['plasmashell'], 'shell', {'QT_IM_MODULE': ''})
elif MODE == 'gnome-wayland':
    os.environ.update(XDG_SESSION_TYPE='wayland', XDG_CURRENT_DESKTOP='GNOME', XDG_SESSION_DESKTOP='gnome',
                      MUTTER_DEBUG_DUMMY_MODE_SPECS='1280x800')
    subprocess.run(['gsettings', 'set', 'org.gnome.desktop.interface', 'enable-animations', 'false'], check=True)
    subprocess.run(['gsettings', 'set', 'org.gnome.desktop.input-sources', 'sources', "[('ibus', 'libpinyin')]"], check=True)
    subprocess.run(['dbus-update-activation-environment', 'DISPLAY', 'XDG_SESSION_TYPE', 'XDG_CURRENT_DESKTOP'], check=True)
    spawn(['ibus-daemon', '--replace', '--xim', '--panel=disable', '--desktop=gnome'], 'ime')
    desktop = spawn(['gnome-shell', '--wayland', '--no-x11', '--wayland-display', 'wayland-test', '--virtual-monitor', '1280x800', '--devkit'],
                    'compositor', {'GDK_BACKEND': 'x11', 'GSK_RENDERER': 'cairo'})
    wait_for_path(Path(os.environ['XDG_RUNTIME_DIR']) / 'wayland-test', desktop)
    os.environ.update(WAYLAND_DISPLAY='wayland-test', QT_QPA_PLATFORM='wayland', GDK_BACKEND='wayland')
    subprocess.run(['dbus-update-activation-environment', 'DISPLAY', 'WAYLAND_DISPLAY', 'XDG_SESSION_TYPE', 'XDG_CURRENT_DESKTOP'], check=True)
else:
    raise ValueError('Unconfigured desktop: ' + MODE)

time.sleep(1)
if desktop.poll() is not None:
    raise RuntimeError('The compositor did not remain running')

# GNOME is captured from its actual native output. The other test compositors
# render into their private Xvfb parent. Neither path reads the host's display.
if MODE == 'gnome-wayland':
    subprocess.run(['gdbus', 'wait', '--session', '--timeout=15', 'org.gnome.Mutter.ScreenCast'], check=True)
    spawn(['python3', '/mnt/gnome_stream.py'], 'native-stream')
else:
    # Feed the read-only live viewer, and retain an actual dynamic recording.
    spawn(['ffmpeg', '-nostdin', '-hide_banner', '-loglevel', 'error', '-f', 'x11grab', '-framerate', '3',
           '-i', ':90', '-update', '1', '-atomic_writing', '1', '-y', '/mnt/frame.jpg'], 'live-view')
    spawn(['ffmpeg', '-nostdin', '-hide_banner', '-loglevel', 'error', '-f', 'x11grab', '-framerate', '12',
           '-i', ':90', '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p', '-y', '/mnt/recording.mkv'], 'recording')

control = WORK / 'control.sock'
control.unlink(missing_ok=True)
server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
server.bind(str(control))
os.chmod(control, 0o600)
server.listen(1)
public_environment = {name: os.environ[name] for name in
                      ['DISPLAY', 'WAYLAND_DISPLAY', 'XDG_RUNTIME_DIR', 'XDG_SESSION_TYPE',
                       'XDG_CURRENT_DESKTOP', 'DBUS_SESSION_BUS_ADDRESS', 'DBUS_SYSTEM_BUS_ADDRESS']
                      if name in os.environ}
(WORK / 'ready.json').write_text(json.dumps({'mode': MODE, 'environment': public_environment}))
try:
    while True:
        connection, _ = server.accept()
        with connection:
            request = json.loads(connection.makefile().readline())
            try:
                if request.get('stop'):
                    connection.sendall(b'{"stopping":true}\n')
                    break
                arguments = request['arguments']
                if request.get('background'):
                    process = spawn(arguments, request['name'], request.get('environment'))
                    result = {'pid': process.pid}
                else:
                    completed = subprocess.run(arguments, env=os.environ | request.get('environment', {}),
                                               capture_output=True, text=True, timeout=request.get('timeout', 20))
                    result = {'code': completed.returncode, 'stdout': completed.stdout, 'stderr': completed.stderr}
                connection.sendall((json.dumps(result) + '\n').encode())
            except Exception as error:
                connection.sendall((json.dumps({'error': str(error)}) + '\n').encode())
finally:
    server.close()
    control.unlink(missing_ok=True)
    # Finish capture before stopping its compositor and media daemons.
    for process in reversed(children):
        if process.poll() is None:
            process.send_signal(signal.SIGINT if Path(process.args[0]).name == 'ffmpeg' else signal.SIGTERM)
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
