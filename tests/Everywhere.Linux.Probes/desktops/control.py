"""Send a command to one owned desktop lab session; never connects to the user's desktop."""
import argparse
import json
import socket
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', type=Path)
parser.add_argument('--background', metavar='LOG_NAME')
parser.add_argument('--stop', action='store_true')
parser.add_argument('arguments', nargs=argparse.REMAINDER)
options = parser.parse_args()
arguments = options.arguments[1:] if options.arguments[:1] == ['--'] else options.arguments
request = {'stop': True} if options.stop else {'arguments': arguments}
if options.background:
    request.update(background=True, name=options.background)
with socket.socket(socket.AF_UNIX) as connection:
    connection.connect(str(options.directory / 'control.sock'))
    connection.sendall((json.dumps(request) + '\n').encode())
    print(connection.makefile().readline(), end='')
