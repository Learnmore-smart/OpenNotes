# OpenNotes.Tests/EditorTextStickySourceTests.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Source-contract NUnit fixture for Task 8 Phase A (`Caelum.Tests`, 3
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

## Constraints

- Source assertions only — no runtime WinUI (the test project references
  the WPF `OpenNotes.csproj`, not `OpenNotes.WinUI.csproj`); behavioral
  coverage lives in `CoreAnnotationUndoTests` via `FakeHost`.
