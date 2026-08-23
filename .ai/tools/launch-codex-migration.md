# tools/launch-codex-migration.ps1
> Last updated: 2026-08-21 | Protection: CRITICAL

## Purpose

Guarded entry point for the real Codex metadata migration. It should only invoke the fail-closed migration after the desktop app and code-mode host are closed.

## Open Threads / Resume Context

- **Status:** completed
- **Intent:** The full Task 47 migration was executed through the guarded launcher; backup and invariant evidence now establish one `Caelum` sidebar project rooted at OpenNotes.
- **Blockers / notes:** No live migration should be rerun. Use the recorded backup and manifest for audit or rollback review; remaining open items are UI/device/external-viewer checks, not project association.

## Important Notes / NEVER Change

- Never bypass the process guard for real AppData.
- Never touch auth, tokens, logs, attachments, or conversation bodies.
- The launcher must invoke `Migrate-AssistantConversations.ps1`, not the narrow single-root compatibility wrapper.
- The launcher must propagate the child migration exit code so a failed guard or rollback cannot be reported as success.
- On success it exits explicitly with code 0; the watcher also propagates this result and records the child output in the temp log.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Completed the guarded live migration; `tools/codex-migration-run.log` and the `.codex` backup manifest record the result. | Codex |
| 2026-08-21 | Pointed the launcher at the full two-root Codex/Antigravity migration and documented the process-guard requirement. | Codex |
| 2026-08-20 | Documented the guarded migration launcher. | Codex |
