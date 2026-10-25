# Linux AT-SPI contract corrections

Scope: `Everywhere.Linux`, its Linux tests and probes. The shared snapshot,
prompt budget, UI, Windows and macOS implementations are unchanged. Desktop
integration remains Hyprland-specific; this change does not claim GNOME/KDE/niri
support or introduce new selection interactions.

## Changes

- Map the provider's explicit READ_ONLY state instead of treating every
  non-editable node as read-only. Empty structural wrappers can then use the
  existing shared projection rules without consuming unnecessary output.
- Preserve tree, tree item, tree table, heading, table header, progress, spinner,
  toolbar, status and radio-menu roles through the existing semantic types.
- Use one bounded native search for direct point hits and child fallback. Search
  the actual trailing children instead of a truncated first-128 prefix, cross
  logical wrappers without Component, and retain visited proxies until search
  disposal so pointer reuse cannot masquerade as a cycle.
- Keep the deepest point target when it has bounds. If its geometry is missing,
  retain the nearest confirmed ancestor; semantic-only queries are unchanged.
- Reuse the existing focus subscription to retain one native focused proxy. Its
  state and parent chain must establish membership in the requested window before
  it is used. No Context, text, key stream or cross-window target is cached.
- Refresh only the matching application's client-side AT-SPI cache and reacquire
  once after an unavailable proxy. This handles lazy bridges replacing their
  initial objects without restarting the application or accessibility service.
- Bound native relation candidates, including null slots, and honor cancellation.
  Virtual descendants remain explicitly queryable within those bounds: skipping
  every MANAGES_DESCENDANTS subtree was rejected after a real Qt tree regression.
  Point/focus fallback avoids exhaustive expansion of those managed containers.
- Return partial-limit, cycle and missing-geometry diagnostics through existing
  query failure metadata. Search bounds remain 256 native candidates and 500 ms
  between calls; libatspi retains its 800 ms normal/1500 ms startup timeout. These
  elapsed-time limits cannot interrupt an already-running native call.

## Validation

On the connected Arch/Hyprland system, using a disposable nested desktop at
scale 1.75 with its own runtime, D-Bus session and profiles:

- 58 Linux unit cases passed, including state/role regression cases.
- Ten synthetic native-protocol cases passed: deep, wide, logical wrapper,
  missing leaf geometry, cycle, limit, sparse managed descendants, wide/deep
  focus, foreign-window focus and stale proxy refresh.
- The old binary missed the expected target in all three baseline point cases.
  A 40-wrapper projection fell from 1395 to 183 characters with the shared
  projection implementation unchanged.
- Real GTK 4 Unicode read/write, semantic button invocation and structural
  observation passed. Real Qt Widgets and Qt Quick exposed the controlled
  fields, button, progress and spinner; the Qt Widgets tree's child item remained
  readable after the bounded-enumeration correction.
- An isolated Chrome profile read the synthetic HTML document with no forced
  renderer-accessibility flag after a persistent assistive client started first.
  This is semantic-tree evidence, not full browser coordinate acceptance.

No private application interface, login credential or model service was used.
Qt accessibility environment overrides were restricted to the controlled test
processes. The retained protocol fixture is intentionally synthetic and does not
substitute for the real toolkit checks above.

## Remaining provider boundaries

The installed Chrome exposed a logical native root of 1898 x 1058 while the web
document reported 3322 x 1700 at scale 1.75. Root-based uniform scaling therefore
failed real acceptance and was removed. The probe now records native root bounds
and failure metadata for reproduction. Fine-grained Chrome hover/capture geometry
at fractional scaling is **not accepted** by this report. A provider-aware fix
requires more evidence than dividing the entire window tree by output scale.

Existing Flutter/PiliPlus embedded-coordinate failures and applications without
an AT-SPI Text interface remain provider limitations. No universal toolkit,
physical multi-monitor, or other-desktop acceptance is claimed.

See the [retained probe instructions](../../tests/Everywhere.Linux.Probes/README.md)
and [validation data](LinuxHyprlandEvidence/atspi-validation.json).
