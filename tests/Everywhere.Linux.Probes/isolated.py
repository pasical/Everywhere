"""Start/stop a disposable nested+headless Hyprland. Never opens DRM devices.

This harness starts no user application and changes no saved compositor configuration.
The nested container is hidden and the parent's previous focus is restored.
"""
import argparse
import json
import os
from pathlib import Path
import resource
import signal
import subprocess
import time

STATE = Path("/tmp/everywhere-hyprland-qa")
RUNTIME = Path("/tmp") / ("ew-qa-" + str(os.getuid()))


def ctl(instance, *args, runtime=None):
    env = os.environ.copy()
    if runtime is not None:
        env["XDG_RUNTIME_DIR"] = str(runtime)
    return subprocess.check_output(["hyprctl", "-i", instance, *args], env=env, text=True, timeout=5).strip()


def query(instance, command, runtime=None):
    return json.loads(ctl(instance, command, "-j", runtime=runtime))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stop", action="store_true")
    parser.add_argument("--parent-signature")
    parser.add_argument("--parent-wayland")
    parser.add_argument("--scale", type=float, default=1.75)
    parser.add_argument("--output-name", default="EVERYWHERE-TEST")
    parser.add_argument("--mode", default="3360x1890@60")
    options = parser.parse_args()
    record = STATE / "instance.json"
    config = STATE / "hyprland.lua"
    if record.exists():
        old = json.loads(record.read_text())
        cmdline = Path(f"/proc/{old['pid']}/cmdline")
        if cmdline.exists() and str(config).encode() in cmdline.read_bytes().split(b"\0"):
            if options.stop:
                os.kill(old["pid"], signal.SIGTERM)
                print("Stopped owned test compositor")
                return
            raise SystemExit("The owned test compositor is already running")
    if options.stop:
        return
    if not options.parent_signature or not options.parent_wayland:
        raise SystemExit("Explicit parent compositor and Wayland socket are required")
    STATE.mkdir(exist_ok=True)
    os.chmod(STATE, 0o700)
    RUNTIME.mkdir(exist_ok=True, mode=0o700)
    if RUNTIME.stat().st_uid != os.getuid():
        raise SystemExit("The disposable runtime directory is owned by another user")
    os.chmod(RUNTIME, 0o700)
    parent = options.parent_signature
    old_focus = query(parent, "activewindow").get("address")
    config.write_text('hl.monitor({output="",mode="1920x1080@60",position="auto",scale=1})\n'
                      'hl.config({general={layout="dwindle",gaps_in=4,gaps_out=10},animations={enabled=false},misc={disable_hyprland_logo=true,disable_splash_rendering=true}})\n')
    env = os.environ.copy()
    parent_socket = Path(options.parent_wayland)
    if not parent_socket.is_absolute():
        parent_socket = Path(env["XDG_RUNTIME_DIR"]) / parent_socket
    # AT-SPI derives socket names from XDG_RUNTIME_DIR and DISPLAY. A separate
    # D-Bus session alone can unlink the live desktop's accessibility socket.
    env.update(WAYLAND_DISPLAY=str(parent_socket), XDG_RUNTIME_DIR=str(RUNTIME), LIBSEAT_BACKEND="everywhere-disabled",
               AQ_DRM_DEVICES="/dev/null", HYPRLAND_NO_SD_VARS="1")
    env.pop("HYPRLAND_INSTANCE_SIGNATURE", None)
    env.pop("DISPLAY", None)
    env.pop("AT_SPI_BUS_ADDRESS", None)

    def no_core():
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))

    with (STATE / "compositor.log").open("w") as log:
        process = subprocess.Popen(["dbus-run-session", "--", "Hyprland", "-c", str(config)],
                                   env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True, preexec_fn=no_core)
    test = None
    for _ in range(100):
        raw = subprocess.check_output(["hyprctl", "instances", "-j"], env=env, text=True).strip()
        try:
            instances = json.loads(raw)
        except json.JSONDecodeError:
            instances = []
        for instance in instances:
            cmdline = Path(f"/proc/{instance['pid']}/cmdline")
            if cmdline.exists() and str(config).encode() in cmdline.read_bytes().split(b"\0"):
                test = instance
                break
        if test:
            break
        if process.poll() is not None:
            raise SystemExit("Owned compositor failed; inspect " + str(STATE / "compositor.log"))
        time.sleep(0.1)
    if test is None:
        process.terminate()
        raise SystemExit("Owned compositor did not become ready")
    record.write_text(json.dumps(test))
    time.sleep(0.5)
    for client in query(parent, "clients"):
        if client["pid"] == test["pid"]:
            ctl(parent, "eval", 'hl.dispatch(hl.dsp.window.move({workspace="special:everywhere-qa",follow=false,window="address:' + client["address"] + '"}))')
    if old_focus and any(window["address"] == old_focus for window in query(parent, "clients")):
        ctl(parent, "eval", 'hl.dispatch(hl.dsp.focus({window="address:' + old_focus + '"}))')
    instance = test["instance"]
    output = json.dumps(options.output_name)
    ctl(instance, "output", "create", "headless", options.output_name, runtime=RUNTIME)
    time.sleep(0.2)
    ctl(instance, "eval", 'hl.monitor({output=' + output + ',mode=' + json.dumps(options.mode) + ',position="0x0",scale=' + str(options.scale) + '})', runtime=RUNTIME)
    ctl(instance, "eval", 'hl.dispatch(hl.dsp.workspace.move({workspace="1",monitor=' + output + '}))', runtime=RUNTIME)
    ctl(instance, "eval", 'hl.monitor({output="WAYLAND-1",disabled=true})', runtime=RUNTIME)
    time.sleep(0.5)
    inherited = dict(item.split("=", 1) for item in Path(f"/proc/{test['pid']}/environ").read_text().split("\0") if "=" in item)
    test_env = {key: inherited[key] for key in ("DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR") if key in inherited}
    # The compositor sets DISPLAY after startup; /proc/PID/environ only exposes
    # its original environment. Resolve its own Xwayland child, never inherit the
    # parent desktop's display into an otherwise isolated test session.
    test_env["DISPLAY"] = ""
    for process_dir in Path("/proc").iterdir():
        if not process_dir.name.isdigit():
            continue
        try:
            arguments = (process_dir / "cmdline").read_bytes().split(b"\0")
            if not arguments[0] or Path(os.fsdecode(arguments[0])).name != "Xwayland":
                continue
            status = dict(line.split(":", 1) for line in (process_dir / "status").read_text().splitlines())
            if int(status["PPid"]) == test["pid"] and len(arguments) > 1:
                test_env["DISPLAY"] = os.fsdecode(arguments[1])
                break
        except (OSError, ProcessLookupError):
            continue
    test_env.update(HYPRLAND_INSTANCE_SIGNATURE=instance, WAYLAND_DISPLAY=test["wl_socket"], GTK_A11Y="atspi",
                    DOTNET_ROOT="/tmp/everywhere-dotnet", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    (STATE / "environment.json").write_text(json.dumps(test_env))
    print(json.dumps({"instance": test, "environment": test_env, "monitors": query(instance, "monitors", runtime=RUNTIME)}))


if __name__ == "__main__":
    main()
