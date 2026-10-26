"""Read-only loopback viewer for synthetic desktop tests. Forward this port over SSH."""
import argparse
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', type=Path)
parser.add_argument('--port', type=int, default=8864)
options = parser.parse_args()
DESKTOPS = {'plasma-x11': 'Plasma X11', 'plasma-wayland': 'Plasma Wayland', 'gnome-wayland': 'GNOME Wayland'}
PAGE = '''<!doctype html><meta charset="utf-8"><title>Everywhere · Desktop port tests</title>
<style>body{margin:24px;background:#14171d;color:#e8edf5;font:16px system-ui}h1{font-size:24px}
main{display:grid;grid-template-columns:repeat(auto-fit,minmax(460px,1fr));gap:20px}
section{background:#222833;border-radius:12px;padding:16px}img{width:100%;background:#101217;min-height:100px}p{color:#abb7ca}</style>
<h1>Everywhere · Desktop port tests</h1><p>Live / last captured frame · synthetic profiles only · read-only viewer</p><main>'''
PAGE += ''.join('<section><h2>' + title + '</h2><img src="/stream/' + key + '"></section>' for key, title in DESKTOPS.items())
PAGE += '</main>'


class Viewer(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/':
            content = PAGE.encode()
            self.send_response(200)
            self.send_header('Content-Type', 'text/html; charset=utf-8')
            self.send_header('Content-Length', str(len(content)))
            self.end_headers()
            self.wfile.write(content)
            return
        name = self.path.removeprefix('/stream/')
        if not self.path.startswith('/stream/') or name not in DESKTOPS:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header('Content-Type', 'multipart/x-mixed-replace; boundary=frame')
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        try:
            while True:
                frame = options.directory / name / 'frame.jpg'
                if frame.exists():
                    pixels = frame.read_bytes()
                    self.wfile.write(b'--frame\r\nContent-Type: image/jpeg\r\nContent-Length: ' + str(len(pixels)).encode() + b'\r\n\r\n' + pixels + b'\r\n')
                    self.wfile.flush()
                time.sleep(.35)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, *_):
        pass


ThreadingHTTPServer(('127.0.0.1', options.port), Viewer).serve_forever()
