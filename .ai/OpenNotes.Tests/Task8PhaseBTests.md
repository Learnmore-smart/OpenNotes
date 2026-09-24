# OpenNotes.Tests/Task8PhaseBTests.cs
> Last updated: 2026-09-24 | Protection: STANDARD

## Purpose

Task 8 Phase B headless coverage — the UI-free halves of the image /
persistent-highlight / text-markup / area-highlight / PDF-text-selection
port plus the WinUI source contract that pins the wiring.

## What it covers

- **`PdfTextSelectionGeometry`** (Core): padded-containment wins /
  nearest-rect fallback / `maxDistance` cutoff / infinite-distance drag
  path / boundless-character rejection (`FindNearestTextOffset`); the
  35%-overlap + 8-DIP-gap merge rule (`ShouldMergeSelectionRects`); merged
  selection quads with range clamping and multi-bound characters
  (`BuildSelectionRects`); union-origin relativization and the empty-input
  contract (`BuildTextMarkupAnnotation`); reverse-drag normalization
  (`NormalizeAreaHighlightRect`); explicit-dims / 40%-fit / degenerate
  sizing (`ComputeImagePlacementSize`).
- **Cross-page image transfer**: `AnnotationContainerTransfer.Move` carries
  `GetImageData`/`SetImageData` payloads to the receiving host, a
  pre-existing target registration stays authoritative, no-payload moves
  still work, and a minimal text-only host relying on the Phase-B default
  interface members remains valid.
- **Highlight undo actions**: `HighlightAddedAction` undo-removes /
  redo-restores; `HighlightRemovedAction` is the mirror (the list starts
  empty — the action models an already-removed highlight).
- **Mixed-op undo chain**: highlight→move→delete committed on one stack
  unwinds strictly LIFO (delete-undo re-adds at the moved position,
  move-undo replays −delta to the origin, highlight-undo delists), then
  redo replays commit order — `FakeImageHost.Positions` keeps a detached
  container's coordinates like a real Grid keeps Canvas.Left/Top.
- **WinUI source contract** (reads `OpenNotes.WinUI` files as text —
  `ReadWinUi` anchors on the dir containing `OpenNotes.WinUI.csproj`):
  `PdfPageControl.xaml.cs` keeps `AddImageAsync`/`_imageDataById`/
  `ImagesChanged`/`AddTextMarkup`/`AddAreaHighlight`/`_highlights`/
  `AddHighlightAnnotation`/`RefreshHighlightsVisuals`/area-drag/
  `SetPdfTextSelection*`/capture/`IAnnotationContainerHost` Phase-B legs +
  `ReleaseResources` sweeping every new registry; `PdfPageControl.xaml`
  keeps the layer order (images under ink, selection above highlights);
  `InkSurface.cs` keeps `AreaHighlight` inside `wantsShape` and OUT of
  `inkCreation` (pen-only safe); `EditorPage.xaml.cs` keeps the six-mode
  flyout, `ActivateHighlighterModeTool`, `ToolType.TextHighlight`/
  `AreaHighlight` routing, the selection pointer pipeline + Ctrl+C copy,
  `PasteClipboardImage*`/`EditorPage_Drop`/`DragOver`/`AllowDrop` wiring,
  and `LoadAnnotationsIntoPagesAsync` covering every Phase-B collection;
  `CollectAnnotations` writes `Images`/`TextMarkups`/`AreaHighlights`/
  `Highlights` with live geometry; `ClipboardImageDecoder.cs` keeps the
  three WPF legs (PNG > Bitmap > EMF).

## Notes

- `FakeImageHost`/`MinimalHost` implement `IAnnotationContainerHost` — the
  former tracks image payloads + a highlight list + `Positions` (so
  `MoveItemsDirectly` mutates container coords the way the real
  `PdfPageControl` moves Grids); the latter exercises the Phase-B default
  members.
- All 30 tests are headless (no UI thread) — run with the standard suite.
