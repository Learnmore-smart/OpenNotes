# T14-B — Home-page tile fixes (WinUI 3)

> Plan + record for the 6.0.3 field report: tile titles/info rows invisible,
> stray vertical accent lines, ugly tile art, no hand cursor.

## Root cause — missing tile titles (verified live)

`ItemsRepeater` + `UniformGridLayout` (`TilesRepeater`, `HomePage.xaml`) is a
**virtualizing** layout: `UniformGridLayoutState.EnsureElementSize` measures
ONLY item 0 and uses its `DesiredSize` as `EffectiveItemWidth×Height`, and
`Algorithm_GetProvisionalArrangeSize` arranges **every** element into that
same slot.

- Item 0 is the **add tile** — its `Button` keeps the 20,12 spacing on the
  *inner* `AddTileContainer` Grid, so its DesiredSize = 200×184 and the whole
  slot is consumed by the button box.
- `FolderTileTemplate`/`FileTileTemplate` put `Margin="20,12"` on the template
  **root Border** — that margin is deducted from the fixed 200×184 slot, so
  the Border renders at **160×160** — exactly the icon grid's height. The two
  label rows (`FileName` ~25 DIP, `InfoText` ~19 DIP) arrange below the
  element's bounds and are clipped → invisible; UIA reports them as
  `BoundingRectangle=[∞;∞;-∞;-∞]`, `IsOffscreen=True`.
- Verified in a Debug run (`tile-dump` instrumentation): `cardH=160` vs
  `desired=200,184`, `tb 'Chem' offInCard=64,169` (below the 160-high box),
  and identical broken UIA rects on the installed 6.0.3 build. Bindings
  themselves were fine — `Text` populated; the problem is purely the arrange
  slot.

### Fix
Set `MinItemWidth="200" MinItemHeight="230"` on the `UniformGridLayout` —
when set, those values *replace* the first-item measurement entirely
(`m_effectiveItemWidth/Height = isnan(min) ? desired : min`), so every tile —
including in selection mode, where the add tile is filtered out of index 0 —
gets a slot big enough for icon + both label rows (content ≈204 + margin 24).

## Remove `HomeMarginRail`
3px ThemeMarginBrush column left of the header (field-reported as a stray
accent line). Collapse the 3/14/* column grid; the header StackPanel keeps the
same visual left inset (24 page margin + 17 rail+gap = 41).

## Tile icon art
WinUI has **no** home-tile thumbnail pipeline (the `ThumbnailCompositor` /
`RenderPageBgraAsync` path is editor-scoped and needs an open `PdfService`
document — too heavy per library tile; noted as a gap). Static redesign:

- **File tile**: paper card (radius `ThemeRadiusCard`, `ThemePaperBrush` +
  `ThemeBorderBrush` hairline) + dog-ear fold + subtle text lines + small
  accent `PDF` pill — replaces the white card + accent spine + red margin
  rail look.
- **Folder tile**: two-tone filled silhouette — back plate + tab in
  `FolderTabBrush`, front plate in `FolderBodyBrush`, thin `FolderLineBrush`
  opening gap. Per-folder `Color` still shows (it drives the folder brushes).
- 120×160 icon grid / viewbox footprint, `IconGrid`/`FolderIconGrid` names and
  all handlers/AutomationIds unchanged.

## Hand cursor — `Controls/CursorExtensions.cs`
Attached prop `CursorExtensions.Hand="True"` → `element.ProtectedCursor =
InputSystemCursor.Create(InputSystemCursorShape.Hand)` — implemented via a
cached `PropertyInfo` on the **non-public** `ProtectedCursor` setter
(direct access is CS0122; same workaround as CommunityToolkit's cursor
extension) + one cached `InputSystemCursor`. `ProtectedCursor` is
pointer-over scoped, so no enter/exit handlers. Applied to 12 HomePage hit
targets (add/folder/file tiles, NavigateUp, both empty-state CTAs, all six
selection-bar buttons) + 8 MainWindow ones (tab cards, Nav Back/Forward/
Home, NewTab, More, Select, Sort). T14-C reuses it for editor chrome.

## Tests / verification — DONE
- `WinUiHomeDialogPolishSourceTests` gained three contracts:
  `HomeTileLayoutReservesLabelRowsAndDropsTheMarginRail`,
  `TileIconsUseFluentFolderAndDocumentArt`,
  `HandCursorHelperIsSharedAcrossHomeAndShell`.
- Live verify (seeded `OPENNOTES_DATA_ROOT` Debug run): `HomeTile_Chem`
  rect finite, `'Chem'`/`'2 items'` TextBlocks `IsOffscreen=False`; file
  tile shows `'root-doc.pdf'`/`'9/27/2026'` + accent `PDF` badge; nested
  folder nav + breadcrumb `Library / Chem` + NavigateUp round-trip;
  context menu opens (8 items); search filter/clear round-trip; selection
  mode toggles (`SelectionSummary` realized); file tile opens EditorPage.
- Screenshot pixel histogram confirmed the new art paints: folder body
  `#3B82F6` (seeded color) + lighter tab tone; file card `#F8F9FA` paper +
  `#2563EB` accent pill + `#D1D5DB` hairline.
- `tools/winui-home-smoke.ps1` showed UIA flakiness in this session
  (stale-element `E_FAIL` after the tile checks; identical failure points
  reproduced on HEAD — environmental, not a T14-B regression).
