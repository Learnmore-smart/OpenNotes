# Migrate-AssistantConversations.ps1

> Last updated: 2026-08-20 | Protection: CRITICAL

## Purpose

Backup-safe, offline migration of assistant conversation workspace/project links from both historical Caelum checkouts to the OpenNotes checkout while keeping the Codex sidebar project name `Caelum`.

## What It Does

- Consolidates Codex rollout `session_meta.cwd`, SQLite thread catalogs, project records, and structural desktop state into the existing project ID rooted at OpenNotes.
- Updates Antigravity trajectory workspace metadata plus structural IDE workspace/history state across `.gemini\antigravity`, `.gemini\antigravity-ide`, and `.gemini\antigravity-cli`.
- Creates timestamped backups and a manifest before modifying each store.
- Refuses to run while Codex or Antigravity is open unless explicitly bypassed for isolated fixtures.

## Important Notes / NEVER Change

- Never globally replace `Caelum`; conversation bodies, titles, prompts, terminal buffers, attachments, and generated artifacts are out of scope.
- Preserve thread/conversation IDs.
- Run against real AppData only after Codex and Antigravity exit.
- SQLite changes must use transactions with `.bail on`, pass `PRAGMA integrity_check`, and restore every backed-up store if any later validation fails.
- Structural path replacement must be boundary-aware, process extended/escaped/forward-slash variants before generic roots, and never rewrite a `Caelum-archive`-style sibling value.
- Every changed Antigravity trajectory row in a database must be migrated in one transaction; a database backup is taken once before its batch.
- Primary Codex `project_roots` rows are scanned to discover old-path project IDs beyond the known historical IDs before duplicate projects are removed.
- The manifest reports the de-duplicated count of project IDs actually removed from the Codex JSON/SQLite stores.
- The process guard must recognize both `Antigravity` and the installed Windows process name `Antigravity IDE`.
- When no `NewProjectId` is supplied, the script derives it from the unique current `project_roots.path = NewRoot` record instead of inventing a replacement ID; fixtures may still pass an explicit ID for isolated databases.
- SQLite backups must include the live `-wal` and `-shm` companions as manifest entries so rollback restores the complete journal state, not only the main database file.
- WAL/SHM snapshots use names outside SQLite's `<destination>-wal` / `<destination>-shm` convention because the CLI `.backup` command may manage those sibling names while opening the backup target.
- The legacy `sqlite\state_5.sqlite` schema is gated: `threads.cwd` is required, while `threads.project_id` is optional because the installed legacy store does not expose that column.
- A named per-user migration mutex and repeated process checks protect every write; `codex-command-runner-*` is treated as a blocking process too.
- Before the manifest is written, final read-only invariants require zero old-root project/thread/catalog rows, zero unassigned current-root rows, zero old global project references, and zero old rollout headers; any violation enters the existing rollback path.

## Open Threads / Resume Context

- **Status:** completed
- **Intent:** Preserve the backup-safe, scoped structural metadata migration and its evidence.
- **Next steps:** Do not rerun the live migration. Audit the recorded backup/manifest if needed, then verify the single `Caelum` sidebar entry and historical-thread continuation from the reopened desktop app.
- **Blockers / notes:** The fixture, real-state dry-run, and guarded live run are green. The historical Caelum folder was already absent and no inspected backup contains its contents. Device and external-viewer checks remain separate.

## Agent Decisions / Thoughts

- **2026-08-20 Codex:** Forward-slash path replacement belongs in `Convert-StructuralText`, alongside the existing escaped path forms. Keep replacement limited to known old roots so arbitrary conversation text is not rewritten.
- **2026-08-21 Codex:** Structural editor state also contains a four-backslash encoded root; map that exact known-root form so nested paths are fully migrated without touching body text.
- **2026-08-21 Codex:** Keep the Codex project display name `Caelum`; OpenNotes is the product/checkout brand, while the sidebar entry remains the compatibility name requested by the migration plan.

## Bug Fixes

| Date | Bug | Cause | Fix |
|---|---|---|---|
| 2026-08-21 | Nested Antigravity editor paths retained Caelum | Structural conversion lacked forward-slash and four-backslash root variants | Replace only those exact variants for each configured legacy root |
| 2026-08-21 | Migration could partially commit or leave one trajectory row unchanged | SQLite CLI continued after statement errors; each trajectory row was committed separately; no outer rollback | Enable `.bail on`, wrap the full migration in backup restoration, and batch all changed rows per database |
| 2026-08-21 | Structural paths could rewrite sibling names or miss extended-prefix normalization | Literal replacements were not boundary-aware and generic roots ran before extended variants | Apply boundary-aware replacements and process URI/extended variants first |

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Completed the live run: 82 primary threads and 35 catalog threads reassociated; one canonical OpenNotes root remains and the backup manifest is retained. | Codex |
| 2026-08-21 | Added WAL/SHM backup restoration, per-user mutual exclusion, repeated process checks, and runner-process detection before real writes. | Codex |
| 2026-08-21 | Kept SQLite sidecar snapshots outside the backup database's managed names and added the legacy `threads` schema gate. | Codex |
| 2026-08-21 | Added the Antigravity CLI conversation root and verified a real-state temporary-copy dry run: canonical `fc720...`, 102 primary rows, 36 catalog rows, 0 unassigned current-root rows, and 30 rollout cwd rewrites. | Codex |
| 2026-08-21 | Added manifest-backed final invariant validation; the real-state dry-run reports all validation counters as zero. | Codex |
| 2026-08-21 | Resolve the canonical project ID from the existing OpenNotes root when running the real migration. | Codex |
| 2026-08-21 | Recognized the installed `Antigravity IDE` process name in the fail-closed guard. | Codex |
| 2026-08-21 | Added scoped structural path variant conversion; fixture passes | Codex |

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Added scoped Codex/Antigravity metadata migration. | Codex |
