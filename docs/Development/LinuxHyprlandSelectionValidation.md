# Desktop links and text-selection follow-up

Validated on 2026-10-25, Asia/Singapore, after `b8797efc` on `linux-hyprland`. Browser fixes were committed first as `a20cad69`; selection work followed separately. No package was installed.

## External links

The login flow used the BCL launcher's shell command; query strings and file names crossed shell parsing. Native-window hyperlinks used a different launcher. The running test installation also inherited private XDG directories: directly launched browsers consequently saw a different configuration from the desktop session. The two observed Chrome desktop entries both invoked the installed Chrome executable; they did not prove a different browser installation.

`LinuxDesktopLauncher` now sends OpenURI through the session desktop portal, using the existing Avalonia D-Bus dependency. It subscribes before calling and keeps the connection alive until the asynchronous Response. Login, application links and native-window hyperlinks share this route. Cancellation/failure returns false, and OAuth reports an opening failure instead of waiting indefinitely for a callback. Local files and unavailable-portal fallback use one `xdg-open` argument without a shell. The separate automation browser remains intentionally independent.

The real desktop portal accepted the public repository URL. Three private-bus probes verified exact delivery of an OAuth-shaped URI containing ampersands, plus signs, percent escapes and shell metacharacters, and correctly handled Response codes 0, 1 and 2. Three Linux regression cases cover fallback argument preservation and nonzero exit status. No private browser tab was inspected, no authentication credentials were entered, and a completed OAuth callback/login is not claimed.

## Palette presentation

The old path parked an ordinary window at (-32000,-32000), then revealed it after rendering. Hyprland constrained its first map to the output center; ordinary compositor border/shadow also surrounded its transparent rectangle.

The palette now prepares at its anchor, with a transparent content wrapper. `IWindowPresentation` provides the native placement barrier. The Hyprland state keeps application-owned popup coordinates authoritative, waits for the mapped geometry and suppresses compositor decorations for this palette only. It reveals content after rendering and placement, without taking initial focus. The editor still acquires ordinary native input when explicitly entered; it is not registered as a system input method.

The retained selection state machine, editor and action objects remain shared. Windows/macOS retain their existing off-screen preparation path. Obsolete appearances cannot reveal a newer appearance or dismiss it after a delayed render. Editor and menu size changes retain the same anchor policy.

## Selection coverage

The monitor now accepts both AT-SPI selection and caret events, coalesces a changing range until quiet, and does not terminate after one unsupported provider observation. It reads selection text without requiring control geometry. Duplicate native/PRIMARY observations do not remap the same gesture twice; source window identity participates in that check.

The optional `wl-paste --primary --watch` path supplies text for applications without usable AT-SPI selection support, including the tested Ghostty. It observes PRIMARY only: it neither reads nor writes the regular clipboard and never synthesizes Copy. An authenticated callback pipe carries bounded Unicode text and captured foreground metadata. The Host rechecks window identity and exclusion policy before publishing. Existing PRIMARY content is ignored on enable. Callback/process/pipe lifetimes and reads are bounded; stopping monitoring terminates owned helpers and releases pending native references.

PRIMARY provides no reliable accessible-object identity. Those observations remain text-only; a later focus query is never converted into a visual anchor. A foreground-window check narrows attribution but does not prove that an arbitrary background PRIMARY producer owns the foreground window. Applications without either usable toolkit selections or PRIMARY offers remain unsupported. Flutter control-coordinate and Ghostty semantic-tree limits from the earlier reports still apply.

`wl-paste` was already installed on the test system. If it or its data-control capability is unavailable, toolkit monitoring remains available; no dependency is installed automatically.

## Native acceptance

| Check | Observed result |
| --- | --- |
| Release application/probe build | Passed; existing ShadUI MinVer metadata warning only. |
| Linux tests | 36 passed, zero failed, including URI handling, bounded Unicode selections and relay metadata round-trip. |
| Focused Core selection tests | 8 passed, zero failed. |
| Real Ghostty mouse selection | Toolbar appeared for selected synthetic terminal text; editor obtained native focus and accepted pasted text. |
| Real Chrome selection | Toolbar appeared for text in the retained synthetic HTML page, without forced renderer accessibility flags. |
| Real GTK selection | Unicode entry selection produced a toolbar after the other applications' selections. |
| Regular clipboard isolation | A synthetic clipboard sentinel remained unchanged through those selection/editor checks. |
| Repeated cross-application selection | Six successive terminal/GTK/browser transitions produced new stable appearances, including selecting the same terminal text again. |
| Center flash | Sixteen sampled center-region images stayed identical to the reference during a new selection anchored near the top. This is sampled-frame evidence, not a guarantee for every possible frame or output setup. |
| Primary desktop | Native Wayland GTK selection passed on the real Hyprland/Caelestia desktop at scale 1.75: pointer (93,249), toolbar (6,265), 642 × 57 logical units. Test windows occupied a separate workspace. |
| Provider/API use | No model request was made. |

Cross-application tests used a disposable 3360 × 1890 output at scale 1.75, with its own runtime directory, session bus and application profiles. Primary-desktop acceptance used separate Everywhere/GTK test instances on HDMI-A-1, logical bounds 2194 × 1234. Other private application interfaces were not inspected. No new physical mixed-monitor or rotated-output acceptance is claimed.

Evidence:

- [Application selection and editor checks](LinuxHyprlandEvidence/selection-apps.json)
- [Repeated selection checks](LinuxHyprlandEvidence/selection-repeat.json)
- [Sampled center frames](LinuxHyprlandEvidence/selection-center-frames.json)
- [Real desktop geometry](LinuxHyprlandEvidence/selection-primary.json)
- [Real desktop toolbar](LinuxHyprlandEvidence/selection-primary-toolbar.png)
- [Ghostty selection and editor](LinuxHyprlandEvidence/selection-terminal-editor.png)
- [Chrome selection](LinuxHyprlandEvidence/selection-browser-toolbar.png)
- [Transferred source comparison](LinuxHyprlandEvidence/selection-source-verification.json)
- [Cleanup and original-profile restart](LinuxHyprlandEvidence/selection-restored-runtime.json)

The retained GTK demo, browser page and portal probes are described in the [probe README](../../tests/Everywhere.Linux.Probes/README.md). They contain synthetic data suitable for further recording. Actual application coverage remains a toolkit capability check, not a blanket claim about every Linux interface.

The tested source comparison covered 18 changed source/probe files without a
mismatch. Owned test windows, input devices, PRIMARY helpers and the nested
compositor were stopped. The final application was restarted with its original
XDG profile, and both Hosts authenticated. Other user browser/terminal instances
were preserved.

## Latest deployment handoff

The Hyprland input observer now dismisses the passive palette on outside clicks,
Escape, typing, scrolling and foreground changes, without consuming native input.
The native regression confirmed outside click delivery, Escape, typing, scrolling,
menu opening and keyboard navigation. A follow-up menu-scroll check failed; popup
global-coordinate reconciliation and editor-leave coverage still need follow-up.
