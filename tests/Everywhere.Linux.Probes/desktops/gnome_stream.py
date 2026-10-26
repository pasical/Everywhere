"""Lab-only native Mutter output feed, unrelated to product capture support.

Uses the owned test session's internal ScreenCast API and PipeWire. No portal
permission or silent-capture capability is claimed for production GNOME.
"""
import os
import signal
import subprocess
from pathlib import Path

import gi
gi.require_version('Gst', '1.0')
from gi.repository import Gio, GLib, GLibUnix, Gst

WORK = Path('/mnt')
if not (WORK / 'session.py').exists() or os.environ.get('XDG_RUNTIME_DIR') != '/mnt/runtime':
    raise SystemExit('Run only inside desktops/launch.py\'s disposable lab.')
Gst.init(None)
bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
loop = GLib.MainLoop()
pipeline = None
recorder = None


def call(path, interface, method, parameters=None):
    return bus.call_sync('org.gnome.Mutter.ScreenCast', path, interface, method, parameters,
                         None, Gio.DBusCallFlags.NONE, 10000, None).unpack()


def frame_ready(sink):
    sample = sink.emit('pull-sample')
    buffer = sample.get_buffer()
    pixels = buffer.extract_dup(0, buffer.get_size())
    temporary = WORK / 'frame-next.jpg'
    temporary.write_bytes(pixels)
    temporary.replace(WORK / 'frame.jpg')
    if recorder is not None:
        try:
            recorder.stdin.write(pixels)
            recorder.stdin.flush()
        except BrokenPipeError:
            loop.quit()
    return Gst.FlowReturn.OK


def stream_ready(_bus, _sender, _path, _interface, _signal, parameters, _data):
    global pipeline, recorder
    node = parameters.unpack()[0]
    (WORK / 'native-stream.json').write_text(str(node))
    recorder = subprocess.Popen(['ffmpeg', '-nostdin', '-hide_banner', '-loglevel', 'error',
                                 '-f', 'image2pipe', '-framerate', '3', '-vcodec', 'mjpeg', '-i', 'pipe:0',
                                 '-c:v', 'libx264', '-threads', '2', '-preset', 'ultrafast',
                                 '-pix_fmt', 'yuv420p', '-y', '/mnt/gnome-native-recording.mkv'], stdin=subprocess.PIPE)
    pipeline = Gst.parse_launch(f'pipewiresrc path={node} ! videoconvert ! videorate ! '
                                'video/x-raw,framerate=3/1 ! jpegenc quality=90 ! '
                                'appsink name=frames emit-signals=true max-buffers=1 drop=true')
    messages = pipeline.get_bus()
    messages.add_signal_watch()
    messages.connect('message::error', lambda _bus, message: (print(message.parse_error(), flush=True), loop.quit()))
    pipeline.get_by_name('frames').connect('new-sample', frame_ready)
    pipeline.set_state(Gst.State.PLAYING)


session = call('/org/gnome/Mutter/ScreenCast', 'org.gnome.Mutter.ScreenCast', 'CreateSession', GLib.Variant('(a{sv})', ({},)))[0]
stream = call(session, 'org.gnome.Mutter.ScreenCast.Session', 'RecordMonitor',
              GLib.Variant('(sa{sv})', ('Meta-0', {'cursor-mode': GLib.Variant('u', 1)})))[0]
bus.signal_subscribe('org.gnome.Mutter.ScreenCast', 'org.gnome.Mutter.ScreenCast.Stream', 'PipeWireStreamAdded',
                     stream, None, Gio.DBusSignalFlags.NONE, stream_ready, None)
call(session, 'org.gnome.Mutter.ScreenCast.Session', 'Start')
for signum in [signal.SIGTERM, signal.SIGINT]:
    GLibUnix.signal_add(GLib.PRIORITY_DEFAULT, signum, lambda: (loop.quit(), False)[1])
try:
    loop.run()
finally:
    if pipeline is not None:
        pipeline.set_state(Gst.State.NULL)
    try:
        call(session, 'org.gnome.Mutter.ScreenCast.Session', 'Stop')
    except GLib.Error:
        pass  # The private compositor may already have shut down.
    if recorder is not None:
        recorder.stdin.close()
        recorder.wait(timeout=5)
