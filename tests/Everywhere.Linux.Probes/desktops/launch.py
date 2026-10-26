"""Launch a disposable desktop using a prepared read-only Arch rootfs.

Usage: launch.py LAB_DIRECTORY BUILD_DIRECTORY plasma-x11
The build directory contains src/Everywhere.Linux and tests/Everywhere.Linux.Probes.
No host display or session bus is shared. Only a DNS configuration file is inherited.
"""
import argparse
import os
import shutil
import subprocess
import uuid
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', type=Path)
parser.add_argument('build', type=Path)
parser.add_argument('desktop', choices=['plasma-x11', 'plasma-wayland', 'gnome-wayland'])
options = parser.parse_args()
if os.geteuid() == 0:
    raise SystemExit('Run the desktop launcher as an ordinary user.')
base = options.directory.resolve()
root = base / 'root.x86_64'
state = base / options.desktop
state.mkdir(exist_ok=True)
if (state / 'control.sock').exists():
    raise SystemExit('This desktop is already running; stop it with control.py first.')
for name in ['home', 'runtime', 'config', 'data', 'cache', 'state']:
    (state / name).mkdir(exist_ok=True)
os.chmod(state / 'runtime', 0o700)
(state / 'machine-id').write_text(uuid.uuid4().hex + '\n')
(state / 'ready.json').unlink(missing_ok=True)
(state / 'passwd').write_text(f'root:x:0:0:root:/root:/bin/bash\ntester:x:{os.getuid()}:{os.getgid()}:Desktop test:/mnt/home:/bin/bash\n')
(state / 'group').write_text(f'root:x:0:\ntester:x:{os.getgid()}:\n')
shutil.copy(Path(__file__).with_name('session.py'), state / 'session.py')
profile = state / 'config/fcitx5/profile'
profile.parent.mkdir(exist_ok=True)
if not profile.exists():
    profile.write_text('[Groups/0]\nName=Default\nDefault Layout=us\nDefaultIM=pinyin\n\n'
                       '[Groups/0/Items/0]\nName=keyboard-us\nLayout=\n\n'
                       '[Groups/0/Items/1]\nName=pinyin\nLayout=\n\n[GroupOrder]\n0=Default\n')
arguments = ['bwrap', '--ro-bind', str(root), '/', '--dev', '/dev', '--proc', '/proc',
             '--tmpfs', '/tmp', '--tmpfs', '/run', '--unshare-user', '--unshare-pid',
             '--unshare-ipc', '--unshare-uts', '--hostname', 'everywhere-desktop-lab']
for source, target in [(state, '/mnt'), (state / 'machine-id', '/etc/machine-id'),
                       (Path('/etc/resolv.conf'), '/etc/resolv.conf'), (state / 'passwd', '/etc/passwd'),
                       (state / 'group', '/etc/group'),
                       (options.build / 'src/Everywhere.Linux/bin/Release/net10.0/linux-x64', '/opt'),
                       (options.build / 'tests/Everywhere.Linux.Probes/bin/Release/net10.0/linux-x64', '/srv')]:
    arguments += ['--bind' if source == state else '--ro-bind', str(source.resolve()), target]
for name in ['DBUS_SESSION_BUS_ADDRESS', 'DBUS_SYSTEM_BUS_ADDRESS', 'DISPLAY', 'WAYLAND_DISPLAY',
             'HYPRLAND_INSTANCE_SIGNATURE', 'GTK_IM_MODULE', 'QT_IM_MODULE', 'XMODIFIERS']:
    arguments += ['--unsetenv', name]
for name, value in {'HOME': '/mnt/home', 'USER': 'tester', 'LOGNAME': 'tester',
                    'XDG_RUNTIME_DIR': '/mnt/runtime', 'XDG_CONFIG_HOME': '/mnt/config',
                    'XDG_DATA_HOME': '/mnt/data', 'XDG_CACHE_HOME': '/mnt/cache',
                    'XDG_STATE_HOME': '/mnt/state', 'LANG': 'C.UTF-8'}.items():
    arguments += ['--setenv', name, value]
arguments += ['dbus-run-session', '--', 'python3', '/mnt/session.py', options.desktop]
with (state / 'session.log').open('w') as log:
    process = subprocess.Popen(arguments, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
(state / 'pid').write_text(str(process.pid))
print('Started', options.desktop, 'PID', process.pid)
