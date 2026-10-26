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
    os.environ.update(XDG_SESSION_TYPE='wayland', XDG_CURRENT_DESKTOP='KDE', KDE_SESSION_VERSION='6')
    desktop = spawn(['kwin_wayland', '--x11-display', ':90', '--width', '1280', '--height', '800',
                     '--socket', 'wayland-test'], 'compositor')
    wait_for_path(Path(os.environ['XDG_RUNTIME_DIR']) / 'wayland-test', desktop)
    os.environ.update(WAYLAND_DISPLAY='wayland-test', QT_QPA_PLATFORM='wayland', GDK_BACKEND='wayland')
    spawn(['plasmashell'], 'shell', {'QT_IM_MODULE': ''})
else:
    raise ValueError('Unconfigured desktop: ' + MODE)

if desktop.poll() is not None:
    raise RuntimeError('The compositor did not remain running')

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
(WORK / 'ready.json').write_text(json.dumps({'mode': MODE, 'environment': dict(os.environ)}))
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
    # SIGINT lets ffmpeg finish its recording before other session children exit.
    for process in reversed(children):
        if process.poll() is None:
            process.send_signal(signal.SIGINT if Path(process.args[0]).name == 'ffmpeg' else signal.SIGTERM)
    for process in children:
        try:
            process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
