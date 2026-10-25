"""Exercise OpenURI on a private D-Bus session without launching a browser.

Build the probes, then run each response case under dbus-run-session:
    dbus-run-session -- python3 tests/Everywhere.Linux.Probes/portal.py success 0
    dbus-run-session -- python3 tests/Everywhere.Linux.Probes/portal.py cancel 1
    dbus-run-session -- python3 tests/Everywhere.Linux.Probes/portal.py failure 2
Requires the same Python GI/GLib runtime as the native GTK fixture.
"""
import json, os, subprocess, sys, time
from pathlib import Path
import gi
gi.require_version('Gio', '2.0')
from gi.repository import Gio, GLib

state = Path('/tmp/everywhere-portal-probe')
state.mkdir(exist_ok=True)
probe = str(Path(__file__).parent / 'bin/Release/net10.0/linux-x64/Everywhere.Linux.Probes')
if sys.argv[1] == 'server':
    code = int(sys.argv[2]); name = sys.argv[3]
    connection = Gio.bus_get_sync(Gio.BusType.SESSION, None)
    connection.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus', 'RequestName', GLib.Variant('(su)', ('org.freedesktop.portal.Desktop', 0)), None, Gio.DBusCallFlags.NONE, -1, None)
    node = Gio.DBusNodeInfo.new_for_xml('''<node><interface name="org.freedesktop.portal.OpenURI"><method name="OpenURI"><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method></interface></node>''')
    loop = GLib.MainLoop()
    def method(conn, sender, path, interface, member, parameters, invocation):
        parent, uri, options = parameters.unpack()
        handle = '/org/freedesktop/portal/desktop/request/' + sender[1:].replace('.', '_') + '/' + options['handle_token']
        (state/(name+'-request.json')).write_text(json.dumps({'uri': uri, 'parent': parent, 'token':options['handle_token']}))
        invocation.return_value(GLib.Variant('(o)', (handle,)))
        def respond():
            connection.emit_signal(sender, handle, 'org.freedesktop.portal.Request', 'Response', GLib.Variant('(ua{sv})', (code, {})))
            GLib.timeout_add(300, lambda: loop.quit() or False)
            return False
        GLib.timeout_add(20, respond)
    registration = connection.register_object('/org/freedesktop/portal/desktop', node.interfaces[0], method, None, None)
    (state/(name+'-ready')).touch(); GLib.timeout_add_seconds(15, lambda: loop.quit() or False); loop.run()
else:
    name, code = sys.argv[1], int(sys.argv[2]); ready=state/(name+'-ready');ready.unlink(missing_ok=True)
    server=subprocess.Popen([sys.executable,__file__,'server',str(code),name])
    for _ in range(100):
        if ready.exists(): break
        time.sleep(.03)
    time.sleep(.1)
    target="https://example.invalid/authorize?client_id=demo&state=a+b%2Fc&scope=openid%20profile&literal=$(touch%20unwanted)#result"
    result=subprocess.run([probe,'--launch-uri',str(state/(name+'-result.json')),target],text=True,capture_output=True,timeout=15)
    request=json.loads((state/(name+'-request.json')).read_text());actual=json.loads((state/(name+'-result.json')).read_text())
    assert request['uri']==target,request
    assert actual['IsLaunched']==(code==0),actual
    assert result.returncode==(0 if code==0 else 1),(result.returncode,result.stdout,result.stderr)
    server.wait(timeout=3);print(name,actual)
