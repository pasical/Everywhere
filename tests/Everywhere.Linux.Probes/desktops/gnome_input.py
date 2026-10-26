"""Inject a JSON sequence only into the owned GNOME lab's virtual input session.

Example: gnome_input.py '[{"pointer":[600,450]},{"button":272},{"key":49}]'
Key codes are Linux evdev codes. This is test infrastructure, not product input support.
"""
import json
import os
import sys
import time
from pathlib import Path
from gi.repository import Gio, GLib

if not Path('/mnt/session.py').exists() or os.environ.get('XDG_RUNTIME_DIR') != '/mnt/runtime':
    raise SystemExit('Run only inside the disposable desktop lab.')
bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
name = 'org.gnome.Mutter.RemoteDesktop'
interface = name + '.Session'


def call(path, method, parameters=None, target_interface=interface):
    return bus.call_sync(name, path, target_interface, method, parameters,
                         None, Gio.DBusCallFlags.NONE, 5000, None).unpack()


path = call('/org/gnome/Mutter/RemoteDesktop', 'CreateSession', target_interface=name)[0]
call(path, 'Start')
try:
    for event in json.loads(sys.argv[1]):
        if 'pointer' in event:
            call(path, 'NotifyPointerMotionRelative', GLib.Variant('(dd)', (-10000., -10000.)))
            call(path, 'NotifyPointerMotionRelative', GLib.Variant('(dd)', tuple(map(float, event['pointer']))))
        elif 'button' in event:
            for state in [True, False]:
                call(path, 'NotifyPointerButton', GLib.Variant('(ib)', (event['button'], state)))
        elif 'key' in event:
            for state in [True, False]:
                call(path, 'NotifyKeyboardKeycode', GLib.Variant('(ub)', (event['key'], state)))
        time.sleep(event.get('delay', .12))
finally:
    call(path, 'Stop')
