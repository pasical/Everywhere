# Linux / Hyprland port

## Runtime boundaries

The `linux-hyprland` implementation starts from `e396bd7c0911df1b1fa5f37cdee388bb42ac93cb`. The current Automation and process-isolation contracts are the migration baseline. `PKGBUILD` remains unchanged.

Main prefers the native Wayland backend through `Avalonia.Wayland` 12.1.3, matching the existing Avalonia version. `UseWaylandWithFallback` retains the previous X11 backend if native initialization fails. Hyprland IPC supplies output topology, visible window order and geometry; AT-SPI supplies application semantics, including native Wayland applications. Output-local coordinate transforms map the picker to compositor logical coordinates. The Automation Host excludes Main's process from picker candidates so overlays cannot select themselves. The shortcut also rejects other owned windows as automatic attachment targets.

`HyprlandWindowingPlatform` decorates the backend before window creation. An initial title containing Main's PID and a per-window ID identifies each owned surface; its normal localized title is restored after mapping. Rules match that exact initial title. Chat floats by default, inherits compositor rounding and retains its last position across hide/show. Interactive selectors and passive effects float at explicit output bounds, disable only their own blur/shadow/rounding/animation, and preserve the underlying tiled geometry. Passive effects use an empty native input region rather than relying on Avalonia hit testing. Owned topmost surfaces are raised when compositor focus changes. These are runtime rules for Everywhere surfaces, not edits to the user's saved configuration.

The native bridge supplies global position, move, activation and topmost handling that ordinary Wayland toplevel APIs do not provide. Desktop coordinates remain logical while native render scaling follows each output: acceptance measured DesktopScaling=1 and RenderScaling=1.75. Managed client shadows are disabled so Hyprland owns the outer shadow and window geometry. Hyprland advertises xdg_toplevel maximized on map to suppress CSD even for ordinary floating windows; the bridge uses actual compositor fullscreen metadata for Avalonia state and translates external floating-window size configures into user resizes. Chat's custom measurement also honors its minimum/maximum dimensions, keeping the content aligned with the native frame.

Avalonia 12.1.3 exposes input transparency for popups but not ordinary toplevels. For native passive windows the bridge invokes the UI-side generated surface proxy's exact `SetHitTestVisible(bool)` overload, which queues the region update on the Wayland worker. It never reads or mutates worker-side protocol state. The fallback uses an empty X11 input shape. Changing the pinned backend version requires revalidating this bridge, native input transparency, menus, geometry and fractional rendering.

`HyprlandScreenSelectionService` shares one selection lifecycle for Screen, Window and Element modes and adds Free mode for screenshots. Pointer motion, wheel events, Tab/Shift+Tab, number/function keys, confirmation and cancellation use that lifecycle. Screenshot mode defaults to Free independently of the last visual-element mode. It freezes each visible non-mirrored output before mapping the selector, shows the frozen image with a dimmed mask and drag rectangle, and crops those same pixels after confirmation. Cropping preserves native density up to the existing capture size limit, including output origins and reverse drags. Masks, tooltips and outlines are absent from the resulting image. Picker leases, pending initialization, frozen images and surfaces are released on completion, cancellation and failure. Chat restores its visibility even if capture throws.

AT-SPI initialization accepts only success/already-initialized return codes. The Automation Host registers an assistive activity listener and drains its GLib context on the native dispatcher. Toolkit bridges can then activate; reading Chromium root attributes additionally activates its lazy renderer tree. Focus and pointer resolution use bounded traversal and window-relative coordinates. Detached embedded providers with unusable extents retain semantic traversal and text but cannot supply a precise hover rectangle. An unavailable bus becomes an explicit unsupported capability with a compositor-window fallback; it must never reach a native libatspi call that aborts Automation Host. This prevents a crash but cannot restore a missing desktop accessibility socket or manufacture inaccessible controls.

Linux peers authenticate Unix socket UID/PID credentials and the executable image. Under Hyprland, RPC identity uses `HYPRLAND_INSTANCE_SIGNATURE`, allowing activation, shortcut relays and Host controls to work across independently launched POSIX jobs without merging different compositor instances.

Input Host retains one binding per chord per installation and updates the callback target after Host recovery. In this Hyprland build, `bind:remove()` removes every binding sharing that chord, so Everywhere never uses it. Inactive owned handles are disabled and their relay commands cleared. Dormant handles remain available for reuse until the compositor configuration is replaced; other owners' bindings are preserved. Lua strings use Lua-compatible escaping rather than JSON Unicode escapes.

## Runtime requirements

Launch from the selected Hyprland session with `HYPRLAND_INSTANCE_SIGNATURE`, `XDG_RUNTIME_DIR`, `WAYLAND_DISPLAY` and the session D-Bus address available. `DISPLAY` is used by the X11 fallback. The implementation targets the Lua IPC/binding/window-rule API used by the validated Hyprland build; it has not been validated against older configuration APIs.

The desktop needs Wayland runtime libraries, AT-SPI (`libatspi.so.0`), GLib/GObject and `grim` for visible screen capture. The fallback additionally uses XWayland and X11 libraries. Desktop notifications/file browsing use `notify-send` and `xdg-open`. Native probes additionally need Python GI with GTK 4. Build with the repository's .NET 10 SDK and restored submodules/dependencies.

```sh
dotnet build src/Everywhere.Linux/Everywhere.Linux.csproj -c Release
dotnet test tests/Everywhere.Linux.Tests/Everywhere.Linux.Tests.csproj -c Release
dotnet build tests/Everywhere.Linux.Probes/Everywhere.Linux.Probes.csproj -c Release
```

## Native validation

Use the disposable compositor created by `tests/Everywhere.Linux.Probes/isolated.py` for the full isolation suite. The retained GTK demo and explicitly scoped `--observe` command may also run on an authorized desktop against controlled windows; see the [demo instructions](../../tests/Everywhere.Linux.Probes/README.md). Supply the parent instance and Wayland socket explicitly for isolation. The harness hides its nested parent surface, creates a headless output, uses a separate D-Bus session **and runtime directory**, resolves only its own Xwayland child's DISPLAY, and records the environment below `/tmp/everywhere-hyprland-qa`. The default 3360x1890 output supports exact scale 1.75. It must never guess the user's compositor, inherit the parent's DISPLAY, or open a DRM device. A separate D-Bus address alone is insufficient isolation: AT-SPI derives its socket path from the runtime directory and display and may unlink a colliding socket.

The GTK fixture exposes a named button, an editable Unicode field and a click counter. `--native <prompt-path>` verifies the real AT-SPI tree, text replacement/readback and semantic invocation. The optional `--provider` flag prompts interactively for a key and sends only the controlled fixture projection. Keys must not be stored in fixtures, settings, reports or logs.

`--picker <result-path>` follows Main's picker through Automation Host and writes the confirmed original-window anchor. `--element` selects the element picker. `--cancel` expects a null anchor and records cancellation; send Escape or right-click while the picker is open. Inspect compositor clients after each run to verify that overlays disappeared.

`--screenshot <result-path>` runs the production Free screenshot path and writes PNG pixels and their dimensions; `--cancel` expects no bitmap. Compare a confirmed region with a pre-selection capture, not a screenshot containing the selection UI. `--passive-overlay <ready-path>` maps the production passive overlay class briefly and records its scaling; click the controlled GTK button underneath it to verify native input transparency and focus retention. `--accessibility-unavailable` must run in a separate process with an explicitly unavailable `AT_SPI_BUS_ADDRESS` and must exit cleanly.

Probe success is not sufficient for Chat acceptance. Also test both attachment-menu commands in the real Main/Chat UI, check attachment admission and effect cleanup, and force a capture-helper failure to check window restoration. No uncontrolled application contents need to be sent to a model for these checks.

First-run validation must use previously nonexistent XDG config/data directories. The runtime resolves SpecialFolder paths with `DoNotVerify` before creating directories. Linux Secret Service lookup runs on a worker; login state resumes on the UI context.

## Capability limits

Applications without accessible AT-SPI semantics retain a window-level projection. Password text is unsupported. Passive global mouse observation and shortcut recording are unsupported; compositor keyboard bindings are available. Screenshot capture represents visible desktop pixels, including occlusion. It is not an unobscured off-workspace window image.

Universal toolkit/browser coverage, accessibility selection monitoring across toolkits and mixed/rotated physical monitor acceptance remain separate validation work. Native rendering, fractional clarity, passive input transparency and negative-origin mixed-density headless outputs have been exercised. The desktop accessibility socket was restored through the user-approved service restart, and a separate controlled GTK test on the primary desktop verified actual semantic access. The installation-wide `--hosts-control shutdown` command is Windows-only; `stop` cooperatively stops Hosts. No Linux package or desktop startup configuration is installed by these probes.

See [the validation record](LinuxHyprlandValidation.md) for observed results and remaining limits.

See the [desktop regression and review guide](LinuxHyprlandRegression.md) for the subsequent element, wheel, placement and screenshot fixes, code navigation and toolkit limits.

External web links use `LinuxDesktopLauncher` and the session OpenURI portal,
including native-window hyperlinks and the OAuth flow. A private Everywhere XDG
profile is not propagated to the browser launched by the desktop service. The
connection stays alive until the portal's Response; cancellation/failure returns
false, and login reports a launch failure instead of waiting for a callback.
When OpenURI is absent, the BCL fallback passes one argument to `xdg-open`
without a shell. The browser automation plugin retains its own separate profile.
