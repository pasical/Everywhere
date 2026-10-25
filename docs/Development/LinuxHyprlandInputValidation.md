# Hyprland input and transient-window validation

Validated on 2026-10-25 (Asia/Singapore), following `e3af0f28` on `linux-hyprland`. The connected system runs Hyprland 0.56.2 and the existing Avalonia 12.1.3 native Wayland backend. No dependency was added in this follow-up.

## Changes and ownership

| Area | Behavior and implementation |
| --- | --- |
| Shortcut recording | `KeyboardShortcutInputBox.Linux.cs` records focused Wayland key events. The Input Host accepts the capture lease and pauses only Everywhere's global bindings. The configured value changes only after a valid key release; Escape, focus loss, detachment and Host cancellation preserve it and release the lease. Left/right modifier keys and additional XKB key symbols are supported. |
| Notification ordering | `RpcConnection` chains notification handlers in transport receive order. Independent thread-pool dispatch previously let a valid sequence overtake a stale notification with the same sequence, causing duplicate callbacks. Requests, responses and transport reading remain independent. A suspended notification making a reverse request is covered by a regression test. |
| Chinese input | Hyprland deactivates its input method on text-input leave while retaining enabled protocol state. Sending enable on re-entry can therefore leave the input method inactive. The owned Wayland window adapter resets the text-input session after queued activation/enter callbacks; pending resets do nothing after deactivation or hiding. X11 activation callbacks retain their existing behavior. |
| Selection toolbar | The Linux helper establishes its floating role before mapping. Clickable palettes suppress initial focus without `no_focus`, which also prevents normal pointer targeting on this compositor. Fully passive effects retain `no_focus`. Policy transitions explicitly clear old rule values. Non-resizable toolbar layout configures do not become user resizes. |
| Reused surfaces | Each map receives a distinct exact title token. A stale IPC observation of an earlier surface cannot restore the visible title before the new surface's floating rule matches. The visible title is restored after observing the current map. |
| Main header | Linux title content receives vertical margins and the main content reserves 32 logical units at the top. Other platform margins are unchanged. |

The source changes retain UI-thread ownership, focused lifecycle cleanup and existing per-window roles. Temporary native tracing was removed. The pre-existing Linux compile barrier in `TextSelectionSettingsTests` was filled with case-sensitive executable-path cases so the Core input tests can run.

## Acceptance

| Check | Observed result |
| --- | --- |
| Actual shortcut settings UI | Eight left/right Ctrl, Alt, Shift and Super cases recorded `K` with the correct modifier and opened Chat globally. Recording an already registered chord did not open Chat. Escape after a partial modifier preserved the previous value. No Input Host state failure was logged. |
| Chinese composition | Fcitx displayed a Pinyin candidate panel and committed `你好` in an isolated session and in a temporary profile on the primary desktop. The primary clipboard was untouched. |
| Chat remapping | Three consecutive cycles used real Ctrl+Space activation, committed `你好`, and closed Chat through its actual close button. Every cycle asserted that Chat disappeared from the compositor before reopening. Compositor addresses may be reused and are not treated as unique map identifiers. |
| Repeated selection | Interleaving Chat remap cycles and real GTK text selection retained native floating toolbars with distinct map title tokens. Clicking the editor obtained native focus and committed `你好`; the GTK target's position and dimensions remained unchanged. |
| Header | Native screenshots show the main title and navigation controls with the reserved top spacing at scale 1.75. |
| Automated checks | 30 Linux tests, 14 focused Core input/settings tests and 64 ProcessIsolation tests passed, with no skips. The RPC suite includes the new cross-operation notification ordering and reverse-request test. |

Evidence: [modifier recording](LinuxHyprlandEvidence/input-shortcuts.json), [Chat remapping](LinuxHyprlandEvidence/input-ime-remap.json), [toolbar interaction](LinuxHyprlandEvidence/input-toolbar.json), [toolbar editor](LinuxHyprlandEvidence/input-toolbar-editor.png), [native candidate panel](LinuxHyprlandEvidence/input-primary-preedit.png), and [main header](LinuxHyprlandEvidence/input-main-header.png). The [source comparison](LinuxHyprlandEvidence/input-source-verification.json) checks the committed files against the tested Linux copy. Test logs are retained for [Linux](LinuxHyprlandEvidence/input-linux-tests.txt), [Core](LinuxHyprlandEvidence/input-core-tests.txt) and [RPC](LinuxHyprlandEvidence/input-rpc-tests.txt).

Tests used a separate Everywhere profile, disposable Hyprland compositor, persistent virtual pointer/keyboard devices and a private Fcitx instance. Devices were created before the application connected. The compact toolbar editor deliberately remains a single-line presentation; multiline expansion is not an acceptance criterion. Candidate panel appearance, text commit, native focus and compositor geometry were checked separately.

## Reproduction and handoff

Run the normal Release test commands in the repository on Linux:

```sh
dotnet test tests/Everywhere.Linux.Tests/Everywhere.Linux.Tests.csproj -c Release
dotnet test tests/Everywhere.Core.Tests/Everywhere.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~KeyboardShortcutInputBoxTests|FullyQualifiedName~InputHostShortcutListenerTests|FullyQualifiedName~TextSelectionSettingsTests'
dotnet test tests/Everywhere.ProcessIsolation.Tests/Everywhere.ProcessIsolation.Tests.csproj -c Release
```

On Hyprland, record left/right modifier chords in Settings → Shortcuts, leave recording, trigger each chord globally and cancel Chat. Repeat recording the currently registered chord and cancel a partial chord with Escape. In Chat, activate Pinyin, inspect the candidates, commit text, close the window and repeat. Select text in the retained GTK fixture, open the toolbar editor, verify Chinese input, and repeat after hiding/reopening Chat. Check that the target remains in its original tile throughout.

The original profile is restored independently of QA settings; its pre-test settings hash is checked before relaunch. The [runtime record](LinuxHyprlandEvidence/input-restored-runtime.json) records the original application's native floating Chat, authenticated Hosts and accessibility socket. The user's existing shortcut enable switches are preserved. Owned test processes and virtual input devices are stopped. No audio was emitted, no model request was made, and no persistent compositor or input-method setting was changed. Existing Flutter/PiliPlus geometry and Ghostty text-provider limits remain as described in the [earlier regression guide](LinuxHyprlandRegression.md).
