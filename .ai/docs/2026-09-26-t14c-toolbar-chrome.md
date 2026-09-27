# T14-C — Editor toolbar/chrome fixes (WinUI 3)

> Plan + record for the 6.0.3 field report: editor toolbar "more broken than
> ever" — cluttered, fragmented by vertical separator lines, icons look
> inconsistent, vertical hairlines elsewhere, no hover cursor anywhere.
> User: "remove all vertical lines" + "when I hover on button give me a hover
> cursor". Follows T14-A (ink hardening) and T14-B (home tiles + shared
> `CursorExtensions.Hand` helper).

## Inventory — vertical separator/divider lines to remove

`OpenNotes.WinUI/Pages/EditorPage.xaml`:
- `ToolbarSeparatorStyle` (Page.Resources ~L241) + the 3 usages inside the
  toolbar StackPanel (~L615 after Redo, ~L727 after Select, ~L757 after
  VersionHistory). Style dies once unused.
- `ZoomSegmentPill` inner hairlines — two `Border Width="1" Margin="0,7"`
  between −/label/+ (~L790, ~L808).
- `CenteredPageJumpHost` inner hairlines — two `Border Grid.Column=1/3
  Width="1" Margin="0,7"` between prev/field/next (~L867, ~L900). The now
  empty `Auto` columns stay (0 width — no child renumber needed).

`OpenNotes.WinUI/Pages/EditorPage.xaml.cs` (`EnsureInlineTextBoxToolbar`):
- 1px `Border` inside `fontButtonGroup` between font −/+ (~L6152) and three
  1px panel separators (~L6293/6302/6311) — the "hairlines elsewhere".

`OpenNotes.WinUI/MainWindow.xaml`:
- Brand accent rail `<Border Width="3" Height="16" ThemeMarginBrush/>`
  (~L348) — the same stray-accent class as T14-B's removed `HomeMarginRail`.

KEEP (surface boundaries / horizontal rules / functional):
- Toolbar pill, page-jump pill, zoom pill, sidebar card, thumbnail card,
  search-panel outlines (BorderThickness=1 surfaces), `ChromeBand` bottom
  edge, `ThumbnailDropIndicator` (2-DIP horizontal), `PopupSectionDivider`
  (1-DIP horizontal rule inside tool flyouts — pinned by
  `WinUiToolFlyoutsSourceTests`), `MenuFlyoutSeparator`s (menu items),
  page-insert `guideLine` (150×2 hover accent), tab accent underline.

## Spacing after removal

Former separator sites leave `Margin="1,0"` on both sides (2 DIP). To keep
the pill segmented (not crowded) the group leaders get
`Margin="5,0,1,0"` — a 6-DIP group gap vs 2-DIP inner gap:
`PenToolButton`, `TextToolButton`, `PenOnlyButton`. `SelectToolButton`
already follows the 152-DIP `PageJumpReservedSpace`.

## Toolbar normalization audit

- Every toolbar `LucideIcon` is 18×18 at the 1.8 ctor default stroke inside
  a 36×36 (page nav 32×32, zoom 34×34 — inside their pill chrome) hit
  target — already consistent. `PdfSearchCloseButton` uses 16×16 (matches
  the outline-invoke glyph convention at 16).
- `Kind` audit: every literal `Kind=` in WinUI XAML resolves through the
  `LucideIcon` table — Undo2/Redo2/PenLine/Highlighter/HiddenInkReveal/
  StickyNote/Eraser/Shapes/Laser/Ruler/MousePointer2/Type/Save/History/
  Minus/Plus/RotateCcw/Chevron*/PanelLeftClose(+Open in code)/Files/
  ListTree/Bookmark/X/GripVertical/Trash2 + menu kinds and `AppTab.Icon`
  (Home/FileText). **No unresolved Kinds found** (a miss would render the
  Circle fallback, not blank). The reported "inconsistent icons" maps to
  the separator fragmentation, not missing glyphs.
- AutomationIds / handlers / T12-B combined-VSM states untouched.

## Hand-cursor rollout (`controls:CursorExtensions.Hand="True"`)

XAML (EditorPage.xaml, 30 sites): all toolbar Buttons/ToggleButtons
(Undo…Rotate ×16), ZoomOut/In + ZoomLabel (Tapped → editor), Previous/Next
Page buttons, SidebarCollapse + 3 nav cells, BookmarkToggle,
PdfSearchClose, outline-invoke glyph button, `ThumbnailCardRoot`,
bookmark-row TextBlock.

XAML (MainWindow.xaml, +4 → 12 total): TabCloseButton + Min/Max/Close
caption buttons.

Code (`CursorExtensions.SetHand(x, true)`):
- `EditorPage.xaml.cs` — page delete + insert-gap "+" buttons; inline text
  toolbar (delete, font −/+, colour, bold, italic); sticky editor
  save/cancel/delete; shared builders `BuildGlyphToggleButton` /
  `BuildTextToggleButton` / `BuildSettingToggleButton` (covers shape grid,
  style toggles, smoothing row, eraser modes, select filters, pen
  behaviour rows); highlighter mode cells; select-flyout width + colour
  swatch buttons; palette cells + recent swatches.
- `PdfPageControl.xaml.cs` — sticky-note marker hit button.
- `PageTemplatePickerDialog.cs` — template cards + browse-folder button.

Skipped deliberately: DEBUG 2×2 invisible seam buttons; ComboBoxes,
Sliders, MenuFlyoutItems, ContentDialog buttons (platform cursor
conventions / template-internal — hand on a menu item or caption ComboBox
is not the ask); `_stickyNoteDragHandle` + ruler/resize handles already
own size/move cursors via `CursorGrid`/`TextResizeHandleElement`.

## Tests

- `WinUiHomeDialogPolishSourceTests.HandCursorHelperIsSharedAcrossHomeAndShell`
  — MainWindow count 8→12; add EditorPage XAML count (30) + anchors.
- New `WinUiFluentPolishSourceTests` contract — toolbar carries zero
  separator chrome: no `ToolbarSeparatorStyle`, no `ThemeMenuSeparatorBrush`
  in the file, no `Width="1"`/`Width="2"`/`Width="3"` vertical bars in the
  ToolbarBorder slice, and `EnsureInlineTextBoxToolbar` has no
  `Width = 1` Borders.
- WPF-rooted fixtures (`EditorToolbarVisualSourceTests` pins
  `ToolbarSeparatorStyle`) read `Pages/` under `OpenNotes.csproj` — the
  WPF files stay untouched.

## Live verify plan

Debug build → seeded `OPENNOTES_DATA_ROOT` launch (recent_files.json per
tools/winui-editor-smoke.ps1) → open PDF → env-gated DEBUG
`RenderTargetBitmap` seam renders `ToolbarBorder` to PNG (screen grabs
can't see compositor content on the locked session — T14-B precedent);
pixel-scan for vertical 1px separator columns; then REMOVE the seam.
Also run tools/winui-editor-smoke.ps1 for the AutomationId contract.

## Result (as implemented)

- **Separators gone.** All inventory items removed; XAML carries zero
  `ToolbarSeparatorStyle`/`ThemeMenuSeparatorBrush` references, zero
  `Width="1".."3"` bars inside the toolbar slice; code-behind has zero
  `Width = 1` separator Borders in `EnsureInlineTextBoxToolbar`.
- **Spacing:** `Margin="5,0,1,0"` on `PenToolButton`/`TextToolButton`/
  `PenOnlyButton` (6-DIP group gap vs 2-DIP inner gap).
- **Cursor rollout:** 31 `CursorExtensions.Hand="True"` in
  EditorPage.xaml; MainWindow 8→12 (`TabCloseButton`, Min/Max/Close);
  17 `CursorExtensions.SetHand` sites in EditorPage.xaml.cs + 1 in
  PdfPageControl.xaml.cs (sticky `hitButton`) + 2 in
  PageTemplatePickerDialog.cs (cards, browse). HomePage stays 12.
- **Icon audit:** every `Kind=` resolves in the `LucideIcon` table —
  no missing/broken glyphs; icons uniform 18×18 in 32–36 DIP targets.
- **Tests:** `EditorChromeRollsOutTheHandCursor` (counts + anchors) and
  `EditorChromeCarriesNoStandaloneVerticalSeparators` added;
  `HandCursorHelperIsSharedAcrossHomeAndShell` updated 8→12.
  80/80 source-contract tests green; Debug build 0 err.
- **Live evidence:** `RenderTargetBitmap` of `ToolbarBorder` →
  1824×104 PNG; pixel column-scan found only the pills' own border edges
  (page-jump host ≈x738/1084, zoom pill ≈x1492/1726 — surface chrome),
  zero standalone interior hairlines; per-glyph ASCII raster shows all
  icons rendered (undo/redo/pen/hi/hidden/sticky/eraser/shapes/laser/
  ruler/select/text/save/history/pen-only/−/100%/+/rotate + ‹ 1/3 ›).
  Seam removed afterwards. `winui-editor-smoke` 60/61 —
  `zoom-in-label-110` fails identically on pristine HEAD (documented
  `ViewChanged` race, pre-existing flake).
- **Cursor honesty:** `GetCursorInfo` reports the default arrow for
  EVERY hover in this session — including a TextBox (I-beam) and the
  window sizing edge — so cursor *shape* can't be observed here at all
  (locked-session limitation). Verified instead: in-app reflection
  readback shows `ProtectedCursor=InputSystemCursor` actually assigned
  on the toolbar buttons (icon child `null` → parent Hand applies, the
  deepest-element-wins rule). The T14-B mechanism is proven; the shape
  can't be screenshotted.
