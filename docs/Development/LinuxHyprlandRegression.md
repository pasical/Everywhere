# Hyprland desktop regression and review guide

Follow-up validation on 2026-10-25, Asia/Singapore, on branch `linux-hyprland`, based on `e396bd7c0911df1b1fa5f37cdee388bb42ac93cb`. This supplements the [earlier validation](LinuxHyprlandValidation.md) with fixes for element selection, wheel sensitivity, Chat placement and saved dimensions. Changes are organized as focused commits on `linux-hyprland`.

## Review entry points

| Area | Implementation and invariant |
| --- | --- |
| Native accessibility | `Automation/AtspiClient.cs` owns libatspi, its GLib context, proxies and assistive listener on one thread. Initialization failure leaves compositor access available. `AtspiNative.cs` contains ABI declarations, ownership helpers and named coordinate/state constants. |
| Element targeting | `HyprlandVisualElementBackend.Accessibility.cs` resolves application/window identity, activates Chromium's lazy tree, finds the focused descendant and performs bounded window-relative hit testing. Provider-self results have a bounded child fallback. |
| Partial capabilities | `AtspiVisualElement.cs` separates semantics from geometry. Detached embedded branches retain text/children/actions while declining unsafe extents. The shared snapshotter traverses a recoverable observation missing only Bounds and retains its failure metadata. |
| Selection lifecycle | `HyprlandScreenSelectionService.cs` selects purpose/mode history; `.Session.cs` owns input, observations and cleanup; `.Overlay.cs` draws; `.Capture.cs` composes pixels. Late observations cannot repaint an earlier mode. |
| Wheel handling | `SelectionModeWheel.cs` accumulates fractional vertical input and limits transitions to one per 180 ms. It resets stale gestures and direction changes, and ignores invalid/zero input. Function keys, digits, numeric keypad and Tab remain available. |
| Native placement | `HyprlandWindowingPlatform.cs` tracks surfaces; `.Window.cs` adapts one backend window. Provisional remap configures cannot become user resizes. `HyprlandWindowState.cs` restores size before position and rereads actual geometry before publishing. |
| Saved Chat size | `SaveWindowPlacementAssist.cs` ignores hidden events, records client size and restores using desktop scaling. Chat's existing user-resize behavior then survives New Chat and restart. |
| Clean screenshots | Owned floating surfaces disable their own compositor animation. Capture waits for their pending unmaps before freezing outputs. Single-output crops use integer native-pixel subsets and the requested output density instead of resampling from rounded logical dimensions. |

Linux paths above are relative to `src/Everywhere.Linux`. Cross-platform placement changes are in `src/Everywhere.Core/AttachedProperties`; partial observation handling is in `src/Everywhere.Automation`. Native query deadlines, traversal limits, ownership transfers and unsupported capabilities are explicit. The refactor adds no package dependency. `PKGBUILD` is unchanged.

## Observed results

Real desktop checks used native Wayland on HDMI-A-1: physical output 3840 × 2160, compositor logical bounds 2194 × 1234, scale 1.75. They used a separate Everywhere profile, the retained synthetic GTK fixture, an isolated Chrome profile with `https://danluu.com/`, a synthetic Ghostty terminal and PiliPlus. Other application interfaces were not inspected.

| Check | Result |
| --- | --- |
| Release application and probe build | Passed; zero errors. ShadUI MinVer warns about transferred submodule Git metadata. |
| Linux tests | 18 passed, zero failed; includes wheel normalization and crops at rounded logical output bounds. |
| Automation tests | 152 passed, zero failed; includes semantic traversal when only Bounds fails and rejection of broader structural failures. |
| Real GTK semantics | Unicode replacement/readback and native button invocation passed; the fixture counter changed. |
| Actual GTK element picker | Outline followed a child control; confirmation admitted an attachment. Cancellation restored Chat and removed selectors. |
| Wheel burst | Eight actual continuous virtual-pointer events changed Element to Window once. Event values are not claimed to equal physical wheel notches; physical touchpad feel remains a user check. |
| Repeated Chat mapping | Five hide/show cycles retained position (300,180), client size 720 × 520. |
| Confirm/cancel placement | Both actual element-menu paths retained the same position and dimensions. |
| New Chat and restart | New Chat, hide/reopen and complete restart retained position (300,180), size 720 × 520. Startup waited beyond Host authentication for shortcut initialization. |
| Screenshot pixels | A 600 × 84 logical crop produced 1050 × 147 pixels at scale 1.75. The prior Chat/menu fade ghost is absent; Chat placement is unchanged. |
| Screenshot comparison | Mean absolute RGB differences from a separate reference were approximately 0.676/0.677/0.684. Focus/frame conditions were not identical; this does not claim pixel-identical captures. |
| PiliPlus semantics | Production backend returned a 4034-character projection containing Search and public video metadata, without querying unsafe embedded geometry. |
| Unavailable accessibility bus | Separate native process reported unavailability and exited 0. |
| Provider/API use | No model request was made during this regression pass. |

Selected evidence: [lifecycle geometry](LinuxHyprlandEvidence/regression-lifecycle.json), [screenshot and semantic acceptance](LinuxHyprlandEvidence/regression-acceptance.json), [element hover](LinuxHyprlandEvidence/regression-element-hover.png), and [attachment admission](LinuxHyprlandEvidence/regression-element-attached.png). JSON geometry records describe compositor observations; interactive screenshots separately show selection and attachment. Unit tests and semantic projections alone do not prove the complete Chat workflow.

## Final handoff verification

The closing pass repeated the real GTK element-menu workflow on the connected desktop. Confirmation admitted the button's accessible label, and both Escape and right-click cancellation restored Chat. All four observations retained position (300,180) and client size 720 × 520; no selector remained. See the [final interaction record](LinuxHyprlandEvidence/regression-finish-interaction.json).

The existing Release binaries passed another 18 Linux tests and 152 Automation tests with no failures or skips. These were `--no-build --no-restore` runs, following the preceding successful Release build. The [source verification](LinuxHyprlandEvidence/regression-source-verification.json) matched 60 local and remote source files, including the Linux adapter, shared placement and snapshot paths, and regression tests. Logs are retained for [Linux](LinuxHyprlandEvidence/regression-linux-tests.txt) and [Automation](LinuxHyprlandEvidence/regression-automation-tests.txt).

The temporary regression Main and Hosts were stopped, and the repaired application was relaunched using the original `20261025-014216` configuration, data and cache directories. No QA settings, attachments or databases were copied into that profile. Its Chinese Chat window is open as a native Wayland floating surface, both Hosts authenticated, the accessibility socket exists, and no selector remains. Its own saved geometry is (491,243), 407 × 605; the separate QA rectangle is not imposed on the original profile. See the [restored runtime record](LinuxHyprlandEvidence/regression-restored-runtime.json).

Startup still reports that the published v0.8.2 update metadata has no compatible Linux asset. This does not prevent the local application or its Hosts from running; update packaging is outside these selection and placement fixes. The GTK fixture and the recording guide below remain available. No sound was played and no persistent compositor or other system setting was changed during this closing pass.

## Remaining toolkit limits

PiliPlus's embedded Flutter nodes expose semantics, but coordinate requests time out in this installed provider. Independent AT-SPI queries reproduced this for SCREEN, WINDOW and PARENT coordinates. The adapter declines geometry below the detached embedded root, preserving later semantic reads. Fine-grained mouse selection/hover outlines in that subtree are **not accepted**. Its compositor window and visible free rectangles remain capturable. The underlying cross-provider cause is unproven; Flutter/PiliPlus code and launchers were not changed.

Installed Ghostty exposes an accessible terminal container but no usable AT-SPI Text interface. Container targeting and screenshots do not prove terminal text extraction. Universal toolkit support and physical mixed/rotated-monitor acceptance remain outside this pass.

Registering an assistive listener allows desktop accessibility to activate toolkit bridges. Chromium can also defer its renderer tree until attributes are requested. Applications started before assistance became active may need to reopen their bridge or restart. Existing browser profiles and launchers were not modified.

## Retained recording demo

Run `tests/Everywhere.Linux.Probes/demo.sh` in the Hyprland session. It opens editable Unicode text, a named button/counter, checkbox, slider, dropdown, scrollable text and labeled color blocks, recording only synthetic values and window-relative geometry. The [demo README](../../tests/Everywhere.Linux.Probes/README.md) provides a recording sequence and an explicitly scoped production-backend observation command.

The [runtime guide](LinuxHyprland.md) documents dependencies, isolated probes, backend constraints and Host control. The earlier validation contains additional isolation/recovery/negative-origin checks; those are historical evidence, not newly repeated physical-monitor tests.
