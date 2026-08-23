# PageBookmarkService

## Purpose

Persists custom page bookmarks outside the PDF, keyed by normalized document path, so bookmarks survive PDF rewrites and application restarts.

## Constraints

- Only page indices and display labels are stored; PDF content is never modified.
- Invalid or missing JSON must degrade to an empty bookmark list.
- Page indices are zero-based and persisted bookmarks are keyed by normalized document path.
- Page edits must use the remapping API after the PDF mutation: inserts shift bookmarks at or after
  the insertion index, deletes remove the deleted-page bookmark and shift later pages, and moves
  transfer the source bookmark while shifting pages in the moved range.
- Exact snapshot replacement is bounded at `MaxBookmarksPerDocument` (100,000 records) to avoid
  unbounded sidecar writes while still covering normal documents.
- A contiguous external import can use the count-aware insert remap so every bookmark at or after
  the insertion point shifts by the full number of imported pages in one persisted update.
- Sidecar writes use a temporary file followed by atomic replacement; external import rollback
  restores both the PDF snapshot and the bookmark snapshot if either half fails before undo is registered.

## Open Threads

- EditorPage page operations call the remapping APIs after successful PDF mutations. The service
  intentionally exposes pure `RemapForInsert`, `RemapForDelete`, and `RemapForMove` methods so
  callers can coordinate PDF and sidecar updates; `RemapForInsert`/`ApplyPageInsert` also accept a
  positive contiguous-page count for external imports. `Replace` restores exact snapshots during
  document undo/redo.
- The sidecar root uses `ProductInfo.GetDataDirectory()` so an explicit `OPENNOTES_DATA_ROOT`
  test process cannot write the user's legacy AppData; the default remains `%LOCALAPPDATA%\Caelum`.

## V5 Completion Status

- Ctrl+M and the Bookmarks sidebar use this path-keyed JSON store; toggle, jump and delete are wired in EditorPage.
- `PageBookmarkPageOperation` and the `ApplyPageOperation` dispatcher provide a single backwards-compatible
  persistence entry point for insert, delete, and reorder operations.
- `Replace` accepts a bounded exact snapshot for undo/redo restoration and canonicalizes it without
  mutating the caller's enumerable.

## Change History

- 2026-08-21: Routed bookmark sidecars through `ProductInfo.GetDataDirectory()` for isolated editor smoke; production compatibility path remains unchanged.
