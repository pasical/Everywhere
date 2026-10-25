"""Run synthetic AT-SPI cases in isolated.py's owned desktop, without model calls.

Run with the environment from /tmp/everywhere-hyprland-qa/environment.json.
The protocol fixture deliberately exercises providers that return themselves,
logical wrappers without Component, and large/deep trees. It does not measure
compatibility with a particular application's own toolkit bridge.
"""
import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path

BASE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, default=Path("/tmp/everywhere-atspi-regression"))
parser.add_argument("--probe", type=Path, default=BASE / "bin/Release/net10.0/linux-x64/Everywhere.Linux.Probes")
parser.add_argument("--baseline", action="store_true", help="Expect the old point traversal to miss three targets")
parser.add_argument("--case", action="append", dest="cases", help="Run only the selected scenario; repeat for multiple cases")
args = parser.parse_args()
expected = json.loads(Path("/tmp/everywhere-hyprland-qa/environment.json").read_text())
for key in ["HYPRLAND_INSTANCE_SIGNATURE", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS"]:
    if os.environ.get(key) != expected[key]:
        raise SystemExit("Run only in isolated.py's disposable desktop")
args.output.mkdir(parents=True, exist_ok=True)
results = []
cases = ["deep", "wide", "logical"] if args.baseline else ["deep", "wide", "logical", "missing-geometry", "cycle", "limit", "managed", "focus-wide", "foreign-focus", "defunct-once"]
for case in args.cases or cases:
    provider_report = args.output / (case + "-provider.json")
    report = args.output / (case + ".json")
    provider_report.unlink(missing_ok=True)
    with (args.output / (case + "-fixture.log")).open("w") as log:
        fixture = subprocess.Popen([sys.executable, str(BASE / "atspi_fixture.py"), case, str(provider_report)], stdout=log, stderr=subprocess.STDOUT)
        try:
            window = None
            for _ in range(100):
                if fixture.poll() is not None:
                    raise RuntimeError("Fixture exited; inspect " + str(log.name))
                clients = json.loads(subprocess.check_output(["hyprctl", "-j", "clients"]))
                window = next((item for item in clients if item["pid"] == fixture.pid and item["mapped"]), None)
                if window and provider_report.exists():
                    break
                time.sleep(.05)
            if not window:
                raise RuntimeError("The owned test window did not map")
            subprocess.run(["hyprctl", "eval", 'hl.dispatch(hl.dsp.focus({window="address:' + window["address"] + '"}))'], check=True, stdout=subprocess.DEVNULL)
            option = "--observe-focused" if "focus" in case else "--observe"
            with (args.output / (case + "-probe.log")).open("w") as output:
                subprocess.run([str(args.probe), option, str(report), window["address"], str(window["at"][0] + 60), str(window["at"][1] + 60)], stdout=output, stderr=subprocess.STDOUT, timeout=45, check=True)
            observed = json.loads(report.read_text())
            hit = observed["Hit"]
            name = hit["Name"] if hit else None
            if args.baseline:
                assert name != "Expected target", (case, name)
            else:
                target = {"focus-wide": "Expected focus", "foreign-focus": "Everywhere AT-SPI foreign-focus",
                          "defunct-once": "Everywhere AT-SPI defunct-once", "managed": "Managed table"}.get(case, "Expected target")
                if case == "limit":
                    assert hit["FailureKind"] == "LimitReached", hit
                else:
                    assert name == target, (case, name, target)
                if case == "missing-geometry":
                    assert hit["Bounds"] and hit["FailureKind"] == "Unsupported", hit
                if case == "cycle":
                    assert hit["FailureKind"] == "ProviderFailure", hit
                if case == "managed":
                    assert "partial" in observed["Tree"].lower(), observed["Tree"]
                    assert json.loads(provider_report.read_text())["calls"].get("managedChildEnumeration", 0) <= 256
            results.append({"case": case, "passed": True, "hitName": name, "failure": hit.get("FailureKind"), "projectionCharacters": len(observed["Tree"])})
            print(json.dumps(results[-1]), flush=True)
        finally:
            fixture.terminate()
            fixture.wait(timeout=5)
(args.output / "result.json").write_text(json.dumps({"baseline": args.baseline, "cases": results}, indent=2))
