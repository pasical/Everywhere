# Linux / Hyprland validation

Native Wayland validation: 2026-10-25, Asia/Singapore. Branch: `linux-hyprland`, based on `e396bd7c0911df1b1fa5f37cdee388bb42ac93cb`. Changes remain in the working tree; no Linux distribution package was installed and no remote branch was pushed. The user approved the matching Avalonia.Wayland dependency and one accessibility-service restart.

For subsequent primary-desktop fixes and current test counts, see the [follow-up regression record](LinuxHyprlandRegression.md). The results below describe the earlier port-validation pass.

## Observed results

| Check | Result |
| --- | --- |
| Linux application and native probe Release builds | Passed, zero errors |
| Linux automated tests | 13 passed, zero failed |
| Previously nonexistent XDG profile startup | Main UI opened; Input and Automation Hosts authenticated |
| Real compositor keyboard binding | Ctrl+Shift+E opened Chat |
| Chat placement | Floating by default; compositor rounding 20 clipped the owned Chat surface |
| Chat geometry | Minimum 400x300 content matches the surface; resizing to 640x480 fills the frame; position retained after hide/show |
| Independent POSIX-session second launch | Activated existing Main and exited successfully |
| Input Host unexpected termination | Replacement Host authenticated, reused the binding and restored hide/open behavior |
| Same-chord foreign binding | Continued receiving actual key events after Everywhere Hosts stopped |
| Cooperative Host stop | Acknowledged; owned bindings disabled and relay command table empty |
| Native Wayland GTK accessibility | Real control tree exposed; emoji, Han characters and combining marks round-tripped |
| Semantic action | Named GTK button invocation changed the fixture counter |
| Production window picker | Actual pointer motion/click returned the original GTK window's anchor and compositor address |
| Picker cancellation | Actual Escape returned null; all picker surfaces disappeared |
| Underlying tiled geometry | Position and size unchanged while each floating picker was mapped |
| Actual Chat visual-element menu | Pointer-selected GTK element admitted as an attachment; selection surfaces cleaned up |
| Actual Chat screenshot menu | Free rectangle drag admitted an image attachment and restored Chat |
| Free screenshot pixels at scale 1.75 | Reverse drag produced 1050x147 pixels, exactly matching the pre-selection region; no mask, outline or tooltip in the image |
| Screenshot cancellation and modes | Escape, right-click, function-key mode switch and actual virtual-pointer wheel events succeeded |
| Passive full-output effect | Floated without changing the GTK tile or stealing focus; a real click passed through and changed the GTK counter from 1 to 2 |
| Screenshot-helper failure | Process-local helper returned 71; error reported, Main survived, Chat restored, no selector remained |
| Unavailable accessibility bus | Separate native process reported unavailability and exited 0 rather than aborting |
| Text clarity | Native clients report xwayland=false; DesktopScaling=1, RenderScaling=1.75; full-resolution Chat/menu screenshots visually verified |
| Negative-origin mixed outputs | Native selectors at (-1280,0) and (0,0), scales 1.5/1.75; negative screenshot 900x126 matched exactly; window and element anchors retained the source PID and negative bounds |
| Primary accessibility recovery | User service socket restored; controlled primary-desktop GTK tree, Unicode round-trip and semantic button action passed |
| Final Main error log | No error-level runtime entries |
| Cleanup | Owned fixture, compositor, Main and Hosts stopped; primary compositor remained |

The native Main/Host recovery suite and actual Chat attachment/screenshot checks used a disposable 3360x1890 output at exact scale 1.75 and compositor rounding 20. A second 1920x1080 output at scale 1.5 occupied logical origin (-1280,0). Its actual screenshot and window/element picker checks passed with both native selectors mapped simultaneously. The primary HDMI-A-1 output independently reported logical size 2194x1234, DesktopScaling=1 and RenderScaling=1.75; its mirror sink did not appear as a duplicate native desktop screen. Unit coverage additionally exercises rotated output bounds, mixed densities, mirrored/disabled outputs, reverse drags, hidden/unmapped windows and visible special workspaces. This is not physical mixed/rotated-monitor acceptance.

The machine used Hyprland v0.56.2 (`efb50993780079460b0cbed1363e2166a2de1d9f`), Everywhere's native Wayland backend, and a native Wayland GTK 4 fixture. Isolated tests had their own D-Bus session, runtime directory and Xwayland display. A separate controlled fixture verified the primary desktop after accessibility recovery. Model/provider calls were not repeated; the earlier bounded DeepSeek Flash tool-selection success is not claimed as a new provider test.

## Corrections verified

- Lua string encoding preserves `+` and uses Lua-compatible control-character escapes.
- Hyprland RPC identity follows the compositor rather than the POSIX job session.
- Input bindings retain one owned handle per chord and update a command table. This avoids the compositor API's chord-wide `remove()` operation, preserves other bindings and recovers from an abruptly terminated Host.
- Linux Chat toggle uses the owned UI focus state before comparing external window identities. Avalonia's X11 XID and the compositor address are different namespaces.
- Picker windows use process-scoped floating rules instead of override-redirect surfaces. This preserves the original tiled geometry and provides keyboard focus. The transparent canvas receives pointer events across the entire output.
- The owned-surface decorator matches exact initial titles, restores localized visible titles after mapping, floats Chat and applies passive native input regions immediately after show. Full-output effect placement is configured after its screen geometry is known.
- The matching Avalonia.Wayland backend renders at fractional output density. The UI-side native proxy's exact Boolean input-region overload was exercised by a real click through a full-output passive surface.
- Chat's custom measure result now obeys MinWidth/MinHeight and maximum constraints. Previously it returned 349.14 client points while the compositor enforced a 400-point window, leaving unused pixels and incorrect target locations.
- Hyprland's protocol-only maximized state is normalized from real compositor metadata. External size configures on mapped floating windows leave SizeToContent, and last-observed position survives uncloaking. The compositor owns the outside shadow, avoiding extra managed client margins.
- Screenshots freeze output images before showing selection UI, maintain separate last modes for picking and capture, support Free drags and crop the frozen pixels at native density. The Chat command restores its cloak state in a finally block and disposes its screenshot after attachment creation.
- libatspi initialization failure is handled before any accessor can invoke the library's fatal bus path. The compositor-window projection remains available when accessibility is unavailable.
- First startup uses unverified SpecialFolder paths before directory creation, and Linux secure-store lookup does not block the UI thread.

During earlier test iterations, the isolated compositor crashed in `CLuaKeybind::push` after bindings were removed/replaced. Inspection of the exact compositor source also showed that `bind:remove()` removes all matching chords and can leave expired weak references. The final application avoids that path. The user's primary compositor was not replaced or restarted.

## Evidence and limits

- [Final native adaptation report](LinuxHyprlandEvidence/wayland-report.json)
- [Actual native Chat element attachment](LinuxHyprlandEvidence/wayland-element-attached.png)
- [Actual native Free screenshot drag](LinuxHyprlandEvidence/wayland-screenshot-drag.png)
- [Native Chat after compositor resize](LinuxHyprlandEvidence/wayland-chat-resized.png)
- [Negative-origin mixed-output acceptance](LinuxHyprlandEvidence/wayland-negative-report.json)
- [Primary desktop accessibility verification](LinuxHyprlandEvidence/primary-accessibility-report.json)
- [Isolated environment restart and cleanup](LinuxHyprlandEvidence/isolation-report.json)
- [Primary test application restarted with its original profile](LinuxHyprlandEvidence/primary-restart-report.json)
- [Structured final native report](LinuxHyprlandEvidence/final-report.json)
- [Main and fixture screenshot](LinuxHyprlandEvidence/final-main.png)
- [Picker screenshot](LinuxHyprlandEvidence/final-picker.png)
- [Runtime, build and probe instructions](LinuxHyprland.md)

The build warns that the transferred ShadUI source lacks Git metadata, so MinVer uses its fallback version. Automatic update discovery warns that release v0.8.2 has no compatible asset; packaging/update publication remains separate work. The test session has no working Secret Service, which may produce a bounded secure-store warning while the UI remains usable.

Dormant shortcut objects remain disabled in the compositor until configuration replacement or compositor exit. They are reused rather than accumulated on every Host recovery; the number follows distinct configured chords. Passive global mouse observation, shortcut recording, full toolkit/browser coverage and physical mixed/rotated-monitor testing remain outside this acceptance. The `--hosts-control shutdown` installation command is Windows-only. Test cleanup stops Hosts cooperatively and terminates only the owned Main process group.

The earlier isolation harness incorrectly shared the desktop runtime directory and inherited DISPLAY. AT-SPI's launcher derives a socket path from those values and unlinks a previous socket when starting; a separate D-Bus session did not prevent a collision. After explicit user approval, one `systemctl --user restart at-spi-dbus-bus.service` restored the advertised `/run/user/1000/at-spi/bus_0` socket. A newly launched primary-desktop GTK fixture then passed real accessibility read/write/action checks. The repaired harness uses `/tmp/ew-qa-1000`, clears inherited accessibility/display values and resolves its own Xwayland child; display `:1` was isolated from primary `:0`. The saved desktop rounding/scaling configuration was unchanged.

The native renderer and its input-region bridge were exercised with the approved `Avalonia.Wayland` 12.1.3 package. They use upstream private/unstable contracts pinned to that version, so upgrades require the native acceptance cases to be rerun. The X11 initialization fallback remains available. No audio playback or saved desktop configuration change was invoked by these tests.

Primary UI testing uses a separate temporary profile and controlled fixture, preserving the user's original chats, model settings and drafts. The final build is restarted with the original config/data/cache directories after cleanup; its separate restart report records the actual running PID and Host state.
