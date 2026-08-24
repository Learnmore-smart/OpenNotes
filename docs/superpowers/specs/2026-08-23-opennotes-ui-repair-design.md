# OpenNotes UI Repair Design

## Status

Approved for direct implementation by the user on 2026-08-23. This is a focused desktop repair pass for the sixteen acceptance items below. The 2026-08-22 visual redesign remains the shell baseline except where this document explicitly replaces a warm light-surface decision with neutral white/gray surfaces. Website, migration, installer, and completed performance work are out of scope.

## Product intent

OpenNotes should read as a quiet document workbench: the PDF is the paper, the surrounding application is a restrained neutral desk, and every tool communicates its actual behavior through a recognizable icon, a small visual preview, a tooltip, and a discoverable UI Automation name. Controls must remain compact enough for a real editor window while preserving keyboard, pen, mouse, localization, and existing file compatibility.

## Confirmed causes and repair boundaries

| Area | Confirmed cause | Required boundary-preserving repair |
|---|---|---|
| Shape undo | `StrokeReplacedAction` holds WPF `Stroke` object references; `ReplaceRecognizedStroke` appends the ideal stroke when the original is not found. | Give each session stroke a stable replacement token and immutable point/attribute snapshots. Replace only an existing token, keep its collection slot, and make undo/redo a no-op when the token was erased; never fall back to `Add`. |
| Save races | `PdfService` has only an instance semaphore, while `EditorPage.AutoSaveAsync` can overlap with another tick or a manual save. | Serialize writes by canonical PDF path in a shared coordinator, coalesce an editor's in-flight autosave, and keep version-sidecar work after a successful atomic PDF replacement. |
| Tool affordances | Laser uses a generic text glyph; shape and highlighter popups use text/`Border` placeholders; the highlighter icon is fixed to `ThemeMarkBrush`. | Use real vector geometry/ink previews, bind the main highlighter mark to the current color, and attach one shared tooltip/UIA contract to static and dynamic controls. |
| Unavailable/obsolete actions | Selection popup retains an Ink Analysis unavailable entry and prompt; toolbar retains Fit Width and Fit Page buttons. | Remove those entry points and their visible localization strings/handlers while retaining ordinary PDF text selection, zoom percentage, zoom +/- and other supported commands. |
| Presets | `PresetSlotsPanel` creates three toolbar buttons and may initialize defaults. | Remove the three-button UI and its initialization path. Keep `AppSettings.PenPresets`, JSON names, defensive clone/sanitize behavior, and old settings files readable/writable without silently deleting the list. |
| Sidebar/navigation | The left rail is a native `TabControl`/`ListBox`/`TreeView` stack with weak hierarchy. | Recompose it as a compact custom rail with explicit Pages/Outline/Bookmarks states, retaining the existing named data controls, page selection, outline navigation, and bookmark service. |
| Surfaces | The light palette is warm paper; page chrome and image layers are not clearly separated. | Make the default light workspace neutral white/gray, add a persisted optional workspace/backdrop choice, and apply color only to chrome/surrounding surfaces—not to PDF bitmap pixels. |
| Transient focus | Runtime popups can be `StaysOpen=true`; there is no `MainWindow.Deactivated` close sweep. | Expose one idempotent `EditorPage.CloseTransientUi` and invoke it for every editor on main-window deactivation. Close Popup, ContextMenu, and ComboBox dropdown state together. |
| Sticky Note | Sticky icons are placed on an `IsHitTestVisible=false` overlay; the editor only has Save-on-close and no Cancel/Delete/move contract. | Make the icon visible and interactive, add bounded drag and delete events, explicit Save/Cancel/Delete semantics, and command actions for create/move/edit/delete with persistence. |
| Hidden Ink | The model/control/editor default to pure white; the toolbar glyph reads as an eye; autosave shares the save race. | Use an opaque, high-contrast gray study mask and a card/answer/reveal semantic icon, preserve transient reveal state, and route saving through the shared coordinator. |

## Acceptance contract

Each item is a release gate. A source/unit test may prove structure, but behavior is not accepted until the listed integration and manual/UIA evidence is captured.

| # | Acceptance behavior | Implementation contract | Required evidence |
|---:|---|---|---|
| 1 | After shape recognition, undo restores the original freehand stroke and the recognized object is actually gone. Erasing after undo must erase the restored object, not an orphan. | `StrokeReplacedAction` stores a session token plus immutable snapshots. `TryReplaceStrokeQuiet` must replace the token in-place and return false rather than append when absent. | Shape pointer smoke: draw a recognized line/rectangle, undo, erase at its location, assert one stroke removed and no ideal duplicate remains. |
| 2 | Repeated shape undo/redo is stable and never duplicates, throws, or changes unrelated strokes. | Undo/redo is token-checked and idempotent; the action does not retain live `Stroke` references and recognition emits an action only after a successful in-place replacement. | Unit sequence `undo → undo → redo → erase → redo → undo`, full test suite, and UIA pointer run. |
| 3 | Laser is represented by a real laser-pointer graphic: a beam/dot geometry with the laser accent, not a generic text glyph or eye. | `LaserToolButton` uses a `Path`/vector resource with a stable accessible name and a non-color active indicator. The ephemeral `LaserInkCanvas` behavior remains unchanged. | Light/dark screenshot review plus UIA discovery of `Editor.LaserToolButton`; pointer smoke confirms the visual laser still fades and is never saved. |
| 4 | Shape popup choices show actual line, rectangle, ellipse, and arrow geometry. | Dynamic shape controls use `Path` geometries sized to the preview box, selected/focus states, localized tooltip, and UIA name; no text-only shape symbols. | UIA enumerates all shape choices; manual screenshot checks each preview and selection state. |
| 5 | Page-number navigation is a compact, keyboard-friendly jump field. | Keep the page indicator and Enter/Escape/LostFocus behavior, but use a compact input/slash/count group with stable width, no dashboard-sized field, and a `PageJump` UIA id. | UIA types first/middle/last page and checks scroll/page selection; manual narrow-window review. |
| 6 | Three preset buttons disappear, while existing `PenPresets` settings remain compatible. | Remove `PresetSlotsPanel`, slot creation, and slot event handlers from the visible editor. Do not remove or rename `AppSettings.PenPresets`; missing fields default safely and existing JSON list entries survive load/save. | JSON compatibility test with a legacy three-slot file; source test proves no visible slot controls; settings sidecar diff shows the list retained. |
| 7 | The highlighter toolbar icon follows the selected highlighter color. | Its mark/underline geometry binds to `_highlighterColor`; active/keyboard states add border/shape cues rather than replacing the color with a fixed theme brush. | UIA opens color popup and screenshot compares at least yellow, green, and blue selections. |
| 8 | Highlighter style preview is an actual mark, with live size and color changes. | Replace text/`Border` placeholder preview with a real `Line`/`Polyline`/ink stroke using current thickness, color, opacity, and rounded caps; update it before the popup closes. | Dynamic preview source test plus manual popup review at small/large sizes and multiple colors. |
| 9 | Ink-to-text unavailable entry and prompt are absent. | Remove the selection-popup command, its click path, and `Editor.InkAnalysisTooltip`/unavailable visible copy. Keep Select filters, shapes, PDF text selection, copy, and search intact. | Localization/source audit confirms no visible unavailable prompt; UIA selection popup exposes only supported actions. |
| 10 | Fit Width and Fit Page are absent. | Remove both XAML buttons, localization labels/tooltips, and dead handlers; retain zoom percentage editing, zoom +/- and scroll behavior. | Source contract and editor UIA toolbar enumeration; manual zoom regression. |
| 11 | Pages/Outline/Bookmarks sidebar is redesigned as a coherent left rail. | Replace the native tab-card presentation with a compact custom rail and explicit selected-state indicators. Keep `ThumbnailListBox`, `OutlineTreeView`, `BookmarksListBox`, page drag/drop, outline navigation, bookmark toggling, collapse, and keyboard focus semantics. | UIA selects all three views and jumps to a page/outline/bookmark; manual visual review at 100% and narrow window. |
| 12 | Light mode is neutral white/gray and workspace/page-surround background is optional. PDF bitmap pixels are never tinted. | Default light tokens are: Window/Desk `#F3F4F6`, Surface/Paper `#FFFFFF`, SurfaceAlt `#F8F9FA`, Canvas/PageSurround `#E5E7EB`, Border `#D1D5DB`, Foreground `#1F2937`, Ink `#2563EB`, Mark `#D9A72E`, Margin `#C2414B`. Persist `WorkspaceBackdrop` (`Neutral`, `Paper`, `Slate`) with `Neutral` as the missing-field default. Apply the selected backdrop to desk/surround chrome only; keep `PdfImage`/`PdfImageOverlay` raw, opaque, and untinted. | Theme unit/source tests, light/dark/high-contrast screenshots, and a known-color PDF pixel comparison before/after backdrop changes. |
| 13 | Losing focus from OpenNotes closes every transient popup/dropdown. | `MainWindow.Deactivated` calls `CloseTransientUi` on all open editors. The method closes all tool/selection/text/sticky/color/version/context popups and every open ComboBox dropdown, safely and repeatedly. | UIA opens each popup/dropdown, switches foreground window, and verifies no popup remains; source test covers the deactivation hook. |
| 14 | Sticky Note is visible, reopens, moves, deletes, and has Save/Cancel/Delete plus undo/redo/persistence. | Sticky overlay is hit-testable only for its note containers; click opens a localized editor with explicit buttons. Save commits one edit action, Cancel restores the opening text, Delete removes the note and pushes an action, drag is page-bounded and undoable, load does not dirty the document, and sidecar/PDF round-trip preserves text/position. | UIA/pointer smoke creates a note, reopens it, drags, saves, cancels another edit, deletes, undoes/redoes, saves, restarts, and verifies text/position. |
| 15 | Hidden Ink autosaves without Unexpected EOF, uses a clear gray study mask, and avoids eye semantics. | New masks default to opaque `#C7CDD4` (dark/high-contrast palettes provide readable alternatives), reveal for the configured duration, and persist hidden. The toolbar uses a card/answer/reveal vector mark and tooltip, not an eye glyph. Autosave/manual save share the path coordinator and an editor in-flight gate. | Hidden Ink smoke draws/reveals/erases/saves/reopens while repeated autosave/manual saves run; no EOF or malformed PDF; screenshot confirms gray mask and non-eye icon. |
| 16 | Toolbar and dynamic buttons have consistent sleek tooltips and UIA metadata. | A shared helper/style assigns `ToolTip`, `AutomationProperties.Name`, stable `AutomationId`, focus ring, and minimum target size to static toolbar controls, dynamic popup buttons, sidebar commands, page actions, Sticky buttons, and resize/drag affordances. | UIA audit enumerates all controls and rejects missing/empty tooltip/name/id; localization verifier finds all visible labels in en/zh/fr. |

## Interaction and persistence details

### Stroke replacement

The page control owns a session-only `Guid` token for every ordinary stroke. Replacement/smoothing/shape code carries the token when it swaps a stroke. The recognition event contains the token, source snapshot, ideal snapshot, and original collection index. The editor undo command contains only those immutable values. A page-level quiet replacement locates the token, checks the expected side (original or ideal), and replaces at the same index; it never appends. If another command has removed the token, the operation returns `false` without resurrecting data. This keeps the existing `IUndoAction` stack and dirty-generation semantics while making erase and repeated undo/redo safe.

### Save serialization

`PdfSaveCoordinator` keys a `SemaphoreSlim` by normalized full PDF path and removes idle gates only after the caller releases them. `PdfService` acquires it around the complete read/strip/rebuild/temp-write/atomic-`File.Move(tempPath, filePath, true)` operation and any required reload. `EditorPage` uses one in-flight task/gate for autosave; a tick that arrives during a save observes the dirty generation and lets the next tick retry rather than starting a second write. Version sidecars are written only after the PDF save succeeds. A failure leaves the document dirty and never publishes a misleading version.

### Sticky Note

Sticky note payloads remain `StickyNoteAnnotation` with DIP `X/Y/Text`; no field rename or version bump is allowed. A note container exposes a stable UIA id, a visible card/paper mark, and a bounded drag handle. The editor owns `StickyNoteCreated`, `StickyNoteMoved`, `StickyNoteEditAction`, and `StickyNoteDeleted` command actions (the existing bulk-item action may be retained only where it preserves note-specific restore semantics). Loading is wrapped by `_isLoadingAnnotations`, so rebuilding a note cannot mark the document dirty. PDF export remains the existing owned `/Text` path and must continue to preserve foreign annotations.

### Hidden Ink

Hidden Ink stays in `PageAnnotation.HiddenInks` and uses the existing `wna_hidden_` ownership prefix, opaque `/CA 1`, `/WNARevealMs`, and transient reveal timers. The new gray color applies to new masks and legacy pure-white masks remain readable/compatible when loaded; saving never writes reveal state. Normal ink, ordinary eraser, and selection do not share the hidden collection.

## Visual/accessibility contract

- Keep the single `MainWindow` + Frame/tab architecture and non-virtualized `PdfPageControl` page stack.
- Use semantic material resources for chrome. Do not put a color matrix, opacity tint, effect, or overlay brush on `PdfImage` or `PdfImageOverlay`.
- Keep all pointer/stylus targets at least 32 DIP and use a stable two-pixel focus ring; selected/pressed states have a non-color cue.
- Dynamic controls are rebuilt during localization refresh and receive metadata after every rebuild.
- Deactivation closure is idempotent and does not close document tabs, discard ordinary text edits, or alter undo history; Sticky Note deactivation specifically follows Cancel semantics for its transient editor.
- No new package, network dependency, PDF viewer, annotation format, or website framework is introduced.

## Non-goals and invariants

1. Do not change Caelum namespace, `%LOCALAPPDATA%\\Caelum`, AppX identity, repository identity, or existing settings/annotation field names.
2. Do not replace strip-and-rebuild loading. PdfiumViewer must continue to receive a PDF stream with OpenNotes-owned `/Ink`, `/FreeText`, `/Highlight`, `/Text`, and `/Stamp` annotations stripped for the in-app view.
3. Do not change DIP coordinates, `72/96` save scale, `96/72` load scale, Y inversion, or third-party annotation ownership prefixes.
4. Do not bypass temporary-file writes or atomic replacement. Structural page operations and `DocumentSnapshotAction` remain as-is.
5. Do not rewrite untouched user changes, migration backups, conversation bodies, authentication/log data, website assets, or installer outputs.

## Verification gate

The repair is complete only when every acceptance row has a named test or source contract, a passing focused test, full `dotnet test`/`dotnet build`, i18n verification, editor UIA smoke, shape/sticky/hidden-ink pointer evidence, and a manual visual review for both neutral light and dark/high-contrast surfaces. Any blocked external/manual check must remain explicitly unclaimed in `.ai/PROJECT_CONTEXT.md`.
