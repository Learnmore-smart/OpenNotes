# tools/repair-codex-project.ps1
> Last updated: 2026-08-20 | Protection: CRITICAL

## Purpose

Backward-compatible repair entry point for the full backup-safe Codex/Antigravity metadata migration. It is intentionally no longer a narrow single-database mutator.

## Open Threads / Resume Context

- **Status:** completed
- **Intent:** The wrapper delegates all normal writes to `Migrate-AssistantConversations.ps1`; its guarded live run is complete and `-ValidateOnly` remains read-only and explicitly guarded.
- **Blockers / notes:** Do not rerun against live AppData; use the retained migration backup for any rollback review.

## Important Notes / NEVER Change

- The helper must not bypass the main process guard or create a second partial migration implementation.
- Historical conversation content and IDs are immutable migration invariants.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Recorded completion of the guarded live migration and canonical-root verification. | Codex |
| 2026-08-20 | Documented the guarded repair helper. | Codex |
