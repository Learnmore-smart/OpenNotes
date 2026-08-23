# Test-MigrateAssistantConversations.ps1

> Last updated: 2026-08-21（rollback, schema-gate, CLI-root, path-boundary and multi-row coverage） | Protection: STANDARD

## Purpose

Fixture-based regression coverage for the Caelum-to-OpenNotes assistant metadata migration.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** Prove project/cwd metadata is migrated while conversation bodies and IDs remain byte-for-byte stable, the existing OpenNotes project ID is selected as canonical, failed runs restore backed-up stores including SQLite WAL/SHM companions, all trajectory rows are handled, and the manifest records zero post-migration invariant violations.
- **Next steps:** Keep fixture green; run the real migration only after the desktop processes are closed.

## Important Notes / NEVER Change

- Tests must keep a literal old path in a conversation body and assert it is not rewritten.
- Fixtures must cover both historical Caelum roots, extended/escaped/forward-slash paths, sibling-name boundaries, known and database-only duplicate Codex project IDs, the legacy `threads` schema without `project_id`, IDE and CLI Antigravity protobuf rows, SQLite sidecar restoration, and rollback after a later SQLite failure.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Added a current-root project record and exercised automatic canonical project-ID selection. | Codex |
| 2026-08-21 | Added manifest SQLite metadata, CLI-root coverage, legacy schema coverage, and final invariant assertions. | Codex |
| 2026-08-20 | Added migration regression-test design. | Codex |
