# Plasma and GNOME Wayland rendering validation

## Scope and environment

Validated on 2026-10-27 in a local Apple Silicon QEMU/HVF virtual machine, with
NixOS 26.05 ARM64, Plasma 6.6.6 and GNOME 50.5. Both desktops ran actual Wayland
sessions. The VM has 4 CPUs, 8 GiB RAM, an 80 GiB sparse disk and a virtio GPU;
Mesa reports llvmpipe software rendering. An isolated ARM64 .NET SDK container
produces the self-contained linux-arm64 application; the application runs in the VM.

The feature branch starts at linux-hyprland commit 83aa0be7. No RPC implementation,
release workflow, authentication configuration or Hyprland backend was changed.

## Reproduced problems and changes

- Main and Chat exposed the wallpaper or other windows through their backgrounds.
  ShadWindow uses theme colors with alpha, but its template does not supply the
  framework transparency fallback layer. StandardWaylandWindowAppearance binds an
  opaque version of the appropriate theme color and follows runtime theme changes.
- Main lacked ordinary window controls. It now uses full framework decorations on
  Plasma/GNOME. The drawn title bar receives an opaque theme background as well.
  Initial finite dimensions are bounded by the screen working area.
- The borderless Chat lacked dependable resize grips. Its edges now request native
  resizing through BeginResizeDrag, with resize cursors and a visible theme border.
- Chat returned to its default dimensions during resizing. Avalonia.Wayland reports
  compositor resize events with WindowResizeReason.Layout. The existing Core latch
  accepted only User, so MeasureOverride discarded the new dimensions. Layout is
  now accepted only while SizeToContent is explicitly Manual; existing automatic
  sizing and minimum-size reset behavior remain in place.
- On KWin, hiding and reopening Chat added a second title bar. In Avalonia 12.1.3,
  WindowImpl.Sink recreates a toplevel and applies its initial decoration mode but
  does not restore the existing sticky client-decoration request. Simply setting
  WindowDecorations again is ineffective because the platform setter returns early
  after that latch is set. The standard-desktop setup uses the public experimental
  ForceDrawnDecorations option with a narrowly scoped diagnostic suppression.
  This keeps Main and Chat decorations consistent across Hide/Show without private
  reflection or KWin rules. Revisit this workaround when upgrading Avalonia.

Appearance bindings are installed once per window and do not keep closed windows
alive. The setup is selected only for PlasmaWayland and GnomeWayland; Hyprland
and X11 keep their existing appearance and decoration negotiation.

## Actual desktop checks

| Check | Plasma Wayland | GNOME Wayland |
| --- | --- | --- |
| Actual desktop session and native Wayland application | Passed | Passed |
| Opaque Main/Chat backgrounds over another window | Passed | Passed |
| Light/dark appearance and theme changes | Passed | Passed |
| Main window controls and resizing/maximizing | Passed | Passed |
| Chat edge/corner resizing without default-size snapback | Passed | Passed |
| Chat Hide/Show preserves size and avoids a second title bar | Passed | Passed |
| Keyboard input retained in an unsent draft | Passed | Passed |

The final decoration build was checked in both desktops, including dark-theme
Hide/Show and resizing. Light backgrounds/theme switching were also checked during
implementation, and the final automated test covers live resource/theme updates.
No model request or authenticated cloud operation was used for these checks.

The VM uses GDM so GNOME no longer emits the screen-locking-unavailable warning
seen under SDDM. The dedicated empty-password test account can require a second
Enter for the additional KWallet PAM conversation. SSH and VNC bind to host loopback.

## Automated checks and baseline comparison

- Final Linux suite: 87 passed, 0 failed, including opaque theme updates and the
  unchanged Hyprland appearance check. Evidence: linux-csd-final.trx.
- Full shared Core suite: 642 passed, 6 failed, total 648. This is not a passing
  full-suite result. Evidence: core-wayland.trx.
- A focused comparison ran the same nine affected/explanatory cases with the
  original ChatWindow source from 83aa0be7, then the feature source in the same
  container. Five failures occur in both. ActiveCapture was a timeout in the
  feature run and passed in the baseline run; treat that additional result as
  unresolved/intermittent rather than claiming identical suites.

| Core failure | Observed evidence |
| --- | --- |
| ReadText_WhenCompositeCrossesMemberBoundary_ContinuesThroughObservedFallback | Existing fixture paging/newline assumption also fails with baseline ChatWindow on Linux |
| InitializeAsync_TavilyApiKeyChanges_PersistsSelectionAcrossReload | SDK test container lacks libsecret-1.so.0; also fails with baseline source |
| CaptureNotifications_ForActiveCapture_UpdateAndFinishScope | Input Host timeout also fails with baseline source |
| RegistrationsAndNotifications_WithDuplicateHandlers_SnapshotOnceAndFanOutLocally | Input Host timeout also fails with baseline source |
| AccountAction_WithoutWindow_UsesApplicationLauncher, GetSupportCommand case | Test build lacks configured AccountBaseUrl; also fails with baseline source |
| ActiveCapture_WhenConnectionDisconnects_IsCancelledLocally | Timeout in full/feature runs, passed in baseline comparison |

The original ChatWindow was mounted read-only for the comparison; the feature
source was rebuilt afterward to prevent reuse of baseline build outputs. RPC was
left untouched rather than changing it to make unrelated tests pass.

Host-side build logs, TRX files, desktop screenshots, NixOS configuration, launch
scripts and decision records are retained in the sibling everywhere-nixos-desktop-vm
directory. Examples: plasma-csd-reopen-resize.png, gnome-csd-reopen.png and
plasma-chat-ready.png. The VM desktop menu exposes Everywhere Desktop Test with
separate Plasma/GNOME test data directories.

## Remaining acceptance boundaries

This validates Main/Chat window rendering in a software-rendered VM. It does not
establish physical-GPU coverage, fractional scaling, multiple monitors, IME
preedit/candidate placement, Windows/macOS runtime acceptance or full desktop parity.
Global shortcuts, selection and passive overlay capabilities remain explicitly
unported in the standard Wayland starter. Cloud login is unavailable in this local
build because official account build constants are absent.

The existing fixed-size welcome guidance can clip in a small restored Main window;
maximizing makes it operable. Its shared dialog layout has not been changed here.