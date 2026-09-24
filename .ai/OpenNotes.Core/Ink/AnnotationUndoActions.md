# OpenNotes.Core/Ink/AnnotationUndoActions.cs
> Last updated: 2026-09-24 | Protection: STANDARD

## Purpose

UI-free undo actions for text/sticky/container annotations (`Caelum.Ink`) —
Task 8 Phase A. The WPF editor keeps these as private nested classes that
replay quiet mutations on `(PdfPageControl page, Grid container)` pairs; to
keep Core UI-free the page is abstracted behind `IAnnotationContainerHost`
(the WinUI `PdfPageControl` implements it with real `Grid`s; headless tests
drive the same actions through a fake host). Container references are
opaque `object`s — the actions only compare identity.

## API surface

- `IAnnotationContainerHost` — quiet container mutations the actions replay:
  `RemoveTextContainerQuiet`/`AddTextContainerQuiet` (no user events, no
  undo recursion; normal mutation notifications SHOULD still fire — the
  WinUI host raises `InkMutated` from every quiet mutator so thumbnails
  refresh through undo/redo and load),
  `ContainsTextContainer` (host-membership probe used by the guarded
  transfer legs),
  `GetOverlayData`/`SetOverlayData` (annotation payload behind a container —
  the sticky model travels with its marker on cross-page moves),
  `GetImageData`/`SetImageData` (Task 8 Phase B — the encoded image bytes
  behind an image container; the payload dict is per-host so a cross-page
  move must hand the bytes to the receiver; default no-op members keep
  text-only test hosts valid),
  `AddHighlight`/`RemoveHighlight` (Task 8 Phase B — re-add/remove a
  persisted text-quad `HighlightAnnotation` on the host's highlight list;
  default no-op for hosts without a highlight layer),
  `SetStickyNotePositionQuiet`/`SetStickyNoteTextQuiet` (page-bounds clamp
  stays in force), `SetTextContainerPositionQuiet`,
  `SetTextContainerBoundsQuiet(bounds, autoWidth?, autoHeight?)` (persist-
  as-auto flags travel with the bounds), `SetTextContentQuiet`,
  `SetTextStyleQuiet(fontSize, r, g, b)`, `SetTextFormatQuiet(snapshot)`,
  `MoveItemsDirectly`/`ScaleItemsDirectly`/`RotateItemsDirectly` (mixed
  strokes+containers legs), `ClearSelection()` (paste-undo drops the
  auto-selected pasted items first).
- `AnnotationContainerTransfer` — internal guarded-transfer helper:
  `Remove`/`Add`/`Move` verify the quiet remove detached, wrap the add
  (a hosted container throws on re-parent in WinUI), probe membership
  afterwards, and roll a failed move back onto the source so a container
  never ends up unhosted. `Move` additionally transfers the image payload
  (`from.GetImageData` → `to.SetImageData`) AFTER the container lands —
  target-side null check keeps an existing registration authoritative
  (WPF `TransferImageData` parity); payload loss is non-fatal.
- `TextFormatSnapshot` / `TextStyleSnapshot` — readonly record structs
  (bold/italic/family/alignment and fontSize+RGB) for before/after captures.
- **Text lifecycle:** `TextBoxAddedAction` (undo removes, redo re-adds),
  `TextBoxDeletedAction` (inverse), `TextEditSessionAction` (one focus
  session's net change — before text captured at edit start; no-op sessions
  never pushed by the caller), `TextStyleChangedAction` (font-size +
  colour), `TextFormatChangedAction` (bold/italic/family/alignment),
  `TextBoxMovedAction` (drag commit before/after positions),
  `TextBoxResizedAction` (bounds + auto-width/height flags before/after —
  undo restores the exact layout mode).
- **Sticky notes:** `StickyNoteAddedAction`/`StickyNoteDeletedAction`
  (marker add/remove), `StickyNoteMovedAction` (drag or keyboard nudge,
  clamped positions), `StickyNoteEditAction` (popup Save commit — undo/redo
  replay text through the quiet setter; when the marker is gone the model
  payload still takes the reverted text so a later restore shows it).
- **Mixed selection transforms (ONE undo step per completed gesture):**
  `AnnotationSelectionMoveAction` (±delta via `MoveItemsDirectly`),
  `AnnotationSelectionResizeAction` (inverse scale about anchor),
  `AnnotationSelectionRotateAction` (±degrees about centre).
- **Persistent text-quad highlights (Task 8 Phase B):**
  `HighlightAddedAction` (a selection-commit highlight was created —
  undo removes from the host list, redo re-adds),
  `HighlightRemovedAction` (inverse). The model lives on the host's
  highlight list — not a container — so replay goes through
  `AddHighlight`/`RemoveHighlight`, not the transfer path.
- **Combined add/remove/cross-page:** `AnnotationItemsAddedAction`
  (paste — undo clears the live selection first, removes strokes
  descending-index + containers; redo restores at captured placements
  then re-adds containers), `AnnotationItemsRemovedAction` (Delete on a
  mixed selection — undo restores placements ascending + containers,
  redo removes descending + containers),
  `AnnotationSelectionCrossPageMoveAction(source, target, sourceHost,
  targetHost, strokes, containers, dx, dy, adjustX, adjustY, placements)` —
  `ExecuteInitialTransfer()` moves strokes between stores (pre-translated
  by the container→page adjust so target visuals build at final coords)
  and transfers containers + their overlay payloads to the target host
  (idempotent; false → skip the undo push); undo/redo replay ±(dx+adjust)
  and move containers back/forth, `LastOperationSucceeded` flags a dead
  stroke the editor then keeps on the stack as a no-op. **Quality pass
  (2026-09-23):** every container leg now rides
  `AnnotationContainerTransfer` — a container the source no longer hosts
  (or the target rejects) is skipped instead of throwing mid-gesture or
  splitting state; `ExecuteInitialTransfer` itself is try/catch-wrapped
  (returns whether anything moved so partial transfers stay undoable);
  the coordinate adjust applies only to containers that actually moved;
  `AnnotationItemsAdded/RemovedAction` gained the same
  `LastOperationSucceeded` contract and the editor checks it on both
  stacks.

## Constraints / NEVER Change

- One action per completed gesture — never per pointer packet; the live
  gesture applies deltas directly and the action only records the total.
- Actions never create further undo entries — every host mutation is the
  quiet path; raising user events would recurse history.
- `IAnnotationContainerHost` stays UI-free: containers are opaque
  `object`s; do not add `Microsoft.UI.Xaml` types here (Core compiles
  for both WPF tests and WinUI).
- Sticky `Id` is stable on load and REGENERATED on paste — the action set
  does not own that rule; the editor's load/paste paths do.
