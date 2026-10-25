#!/usr/bin/env bash
# Launch only the synthetic demo; it does not start or stop Everywhere.
set -euo pipefail
probe_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec env GTK_A11Y=atspi python3 "$probe_directory/fixture.py" "$@"
