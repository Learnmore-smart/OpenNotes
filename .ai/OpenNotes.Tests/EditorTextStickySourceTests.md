# OpenNotes.Tests/EditorTextStickySourceTests.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Source-contract NUnit fixture for Task 8 Phase A (`Caelum.Tests`, 4
tests) — pins the WinUI text/sticky wiring so a later refactor cannot
silently drop pieces the WPF editor relies on. Unlike the older source
tests (which anchor on the WPF `OpenNotes.csproj` tree), this fixture
walks up to the solution root and reads `OpenNotes.WinUI/` — the first
source contract on the port itself.

## Coverage map

- `EditorPageWiresTextToolStickyPopupAndUndoActions` — `Pages/EditorPage.xaml.cs`:
  tool wiring (`page.SetMode(_currentTool == ToolType.Text)`),
  `LoadAnnotationsIntoPages` + `_isLoadingAnnotations` quiet load,
  `CreateTextBox`, `page.AddStickyNote(note)`; the text undo surface
  (`TextBoxAdded`/`Deleted`/`EditSession`/`Moved`/`Resized`/`StyleChanged`/
  `FormatChanged` + `BeginTextEditSession`/`CommitTextEditSession`/
  `NudgeSelectedTextBox`); the inline toolbar (`EnsureInlineTextBoxToolbar`,
  `PositionInlineTextBoxToolbar`, all eight `Editor.TextToolbar.*`
  AutomationIds); the sticky popup lifecycle (`PageControl_StickyNote*`
  handlers, `OpenStickyNoteEditor`, Save/Cancel/Delete paths, the four
  `StickyNote*Action`s, `Sticky.Editor.DragHandle` + `Sticky.Save`/
  `Cancel`/`Delete` ids); the mixed-selection undo set
  (`AnnotationSelection*`/`AnnotationItems*`/`AnnotationSelectionCrossPageMoveAction`);
  the clipboard pipeline (`CopySelection`/`CutSelection`/`PasteSelection`/
  `HasPasteableClipboard` + `Editor.Action.Copy`/`Paste` menu items);
  collectors (`CollectAnnotations`, `GetTextData`, `GetStickyNoteData`);
  and lifecycle teardown (`CancelStickyNoteEdit`, `DeselectTextBox`).
- `PdfPageControlExposesOverlaySurfaceAndQuietMutators` —
  `Controls/PdfPageControl.xaml.cs`: `SetMode(bool isTextMode)`,
  `TextOverlayPointerPressed`/`BackgroundPointerPressed`,
  `AddStickyNote(StickyNoteAnnotation)`, the quiet mutator set
  (`RemoveTextContainerQuiet`/`AddTextContainerQuiet`/
  `SetStickyNotePositionQuiet`/`SetStickyNoteTextQuiet`/`SetOverlayData`/
  `GetOverlayData`), collectors (`GetTextData`/`GetStickyNoteData`/
  `TryGetTextAnnotation`), the attached auto-size DPs
  (`TextAnnotationAutoWidth/HeightProperty`), sticky events
  (`StickyNoteActivated`/`Moved`/`DeleteRequested`,
  `BuildStickyNoteContextMenu`,
  `RefreshStickyNoteContextMenuLocalization`), selection participation
  (`SelectedTextContainers`, `IAnnotationContainerHost`, the three
  `*ItemsDirectly` transforms).
- `CoreAnnotationUndoLedgerCoversTheWpfActionSet` —
  `OpenNotes.Core/Ink/AnnotationUndoActions.cs`: the
  `IAnnotationContainerHost` method contract and every action class
  name, so a rename/removal anywhere in the chain fails a test instead
  of silently regressing.
- `SpecFixContract_HitTestEscapeOrderingAndTransientSweep` (spec-fix
  contract, 2026-09-23) — positional assertions, not just containment:
  the `TextResizeHandleElement` creation block must assign a `Background`
  (panels are pointer-dead without one); the generic
  `CloseTransientUi("escape")` + `ActivateTool(ToolType.None)` branch in
  `EditorPage_PreviewKeyDown` must sit after the resize-restore branch
  but BEFORE the `if (textInputFocused)` bail; `SetHostActive` must call
  `CloseTransientUi("inactive editor")` before the
  `_isHostActive == isActive` no-op guard; `ReleaseResources` shares the
  sweep via `CloseTransientUi("release")`; page `CancelInteraction()`
  must cancel the sticky drag before `ClearShapePreview()`.
- `SpecFixContract_ToolbarFocusCaptureOwnershipAndGestureGuards`
  (quality pass, 2026-09-23) — pins the whole fix batch positionally:
  `IsInteractiveEditorChrome` (ancestor walk over `_inlineTextBoxToolbar`
  + `ButtonBase`/`ComboBox`/`SelectorItem`/`Slider`/`MenuFlyoutItem` +
  non-selected `TextBox`) must precede the nudge and Delete/Back branches
  in `PreviewKeyDown`; the resize capture releases
  `_resizingTextHandleElement` (not the container) in `CancelTextResize`;
  `CancelSelectionInteraction` releases `SelectionOverlayCanvas`
  captures; the per-box `SizeChanged` hook is Loaded/Unloaded-owned;
  Core has `AnnotationContainerTransfer` + `ContainsTextContainer` (with
  the `PdfPageControl` explicit impl); undo/redo restore-bounds cancels;
  `ActivateTool` cancels the live drag when leaving Text; `_dragPointerId`/
  `_textResizePointerId`/`_stickyDragPointerId` guards exist; the
  cross-page drop folds `clamped` into `effDx`/`effDy` and
  `PasteSelection` clamps via `TextAnnotationGeometry.ClampToPage`;
  `ApplyTextContainerBounds` carries no synchronous layout call;
  `MoveItemsDirectly` coalesces through `QueueSelectionVisualsUpdate`
  while `CompleteSelectionGesture` rebuilds synchronously; the sticky
  flyout keeps one path (`flyout.ShowAt`, no `hitButton.ContextFlyout`);
  quiet mutators raise `InkMutated`; `CommitTextEditSession` gates on
  `sessionId != _loadSessionId` + `!ReferenceEquals(page, sessionPage)`.

## Constraints

- Source assertions only — no runtime WinUI (the test project references
  the WPF `OpenNotes.csproj`, not `OpenNotes.WinUI.csproj`); behavioral
  coverage lives in `CoreAnnotationUndoTests` via `FakeHost`.
