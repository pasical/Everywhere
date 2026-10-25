# Hyprland manual demo and native probes

Run the retained GTK demo from a terminal in the desktop session:

```sh
./tests/Everywhere.Linux.Probes/demo.sh
```

Python 3, PyGObject and GTK 4 must already be installed. No package installation,
compositor configuration edit, model API key or model request is needed. The demo
records only its synthetic values and widget geometry in
`/tmp/everywhere-demo/fixture.json`. Pass another JSON path as the first argument
to keep records from separate recording sessions. A floating window around 900 × 760
logical pixels gives the controls and color strip enough room.

## Suggested recording sequence

1. Resize and move Chat to an easily recognizable rectangle. Record the position
   and size before and after every confirmation and cancellation.
2. Open **Add attachment → Pick visual element**. Use F3 and hover over the entry,
   button text, checkbox and long document. The outline must follow the selected
   accessible object; the button's label and button are distinct objects.
3. Confirm the entry, change its value with **Change live text**, then send a
   request that reads the attached context. This distinguishes live semantics
   from the preview bitmap. An assistant is needed only for this last step.
4. Cancel with Escape and right-click. Repeat an immediate delete/re-add, a
   selection followed by New Chat, and hide/show during the flying animation.
   Accepted attachments survive presentation cancellation; cancelled selections
   add nothing and leave no input-blocking surface.
5. Compare one wheel notch with a smooth touchpad gesture. F1/F2/F3 always select
   Screen/Window/Element; F4 is available only during screenshot selection.
6. Capture the color strip in Free mode, including a reverse drag. Compare PNG
   dimensions with the output density and inspect the pixels for selector UI.
7. Close and reopen Chat, create a new conversation, then restart Everywhere.
   A manually chosen Chat size must remain unchanged.

Use a separate Chrome profile with a public page for browser checks. An
application that never registers with AT-SPI cannot expose controls to Everywhere;
its compositor window remains capturable. Do not count that fallback as a
successful element or text-read test. Some Chromium builds require accessibility
to be active before their process starts; `--force-renderer-accessibility` and
`ACCESSIBILITY_ENABLED=1` are useful controlled-test launch options, not a reason
to edit the user's browser launcher or profile.

## Production-backend observations

Build the probes using the commands in
[`LinuxHyprland.md`](../../docs/Development/LinuxHyprland.md). From the selected
Hyprland session, supply an explicit visible test-window address and a point
inside it:

```sh
./tests/Everywhere.Linux.Probes/bin/Release/net10.0/linux-x64/Everywhere.Linux.Probes \
  --observe /tmp/everywhere-demo/observation.json 0xWINDOW_ADDRESS X Y
```

The probe refuses to inspect a different window covering that point. It records
that window's tree, the production backend's hit and its geometry. Use only a
controlled window: its tree can contain application text. This probe does not
prove interactive selection or Chat attachment admission; check those in the
real UI as well.
