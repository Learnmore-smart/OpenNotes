# OpenNotes.WinUI/Controls/CursorExtensions.cs
> Last updated: 2026-09-27 (T14-B — home tiles) | Protection: STANDARD

## Purpose
`Caelum.Controls.CursorExtensions` — a static attached-property helper that
gives any `UIElement` the **Hand** cursor via
`controls:CursorExtensions.Hand="True"`. Created for the T14-B home-tile
polish (tiles, breadcrumb, empty-state CTAs, selection bar, and the
MainWindow nav/tab/toolbar cluster); T14-C will reuse it for editor chrome.

## What It Does
- Registers the attached DP `Hand` (`bool`, `RegisterAttached`); when set
  `True` the element's cursor becomes the hand while the pointer is over it,
  `False` (or unset) restores the inherited cursor.
- Writes `UIElement.ProtectedCursor`, which is **pointer-over scoped** — the
  framework applies the cursor inside the element's hit-test region and
  restores on exit — so no `PointerEntered`/`Exited` handlers are needed. It
  flows through the visual tree: the deepest element under the pointer that
  defines a cursor wins, so setting it on a card root covers every child.
- `ProtectedCursor` is a `protected` property (WinUI exposes it for control
  subclasses only), so the helper invokes the non-public setter through a
  cached `PropertyInfo` (`BindingFlags.Instance | NonPublic`) — the same
  workaround CommunityToolkit's WinUI cursor extension uses until the
  promised public `Cursor` property ships. `typeof(UIElement)` is the
  declaring type; the property is never absent in WASDK ≥ 1.5.
- One `InputSystemCursor` (`InputSystemCursorShape.Hand`) is cached in a
  static field and shared by every consuming element — cursors are
  immutable, so no per-element allocation. UI-thread only, like all DP
  callbacks.

## Important Notes / NEVER Change
- Do NOT wrap the reflection in "try again later" logic — `GetProperty`
  runs at static-init; if `ProtectedCursor` were ever renamed the helper
  should fail loudly (null-check + silent skip today only covers the
  hypothetical missing-member case).
- Keep the setter call on `PropertyInfo` — a public `Cursor` property does
  not exist yet; direct `element.ProtectedCursor =` does not compile
  (CS0122).

## Open Threads / Resume Context
- **Status:** GREEN — applied to 12 hit targets in `HomePage.xaml` + 8 in
  `MainWindow.xaml`; source contracts in
  `WinUiHomeDialogPolishSourceTests.HandCursorHelperIsSharedAcrossHomeAndShell`.
- T14-C: apply the same attribute to editor chrome/tab hit targets.
