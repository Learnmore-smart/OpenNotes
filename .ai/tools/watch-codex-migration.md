# tools/watch-codex-migration.ps1

> Last updated: 2026-08-21 | Protection: CRITICAL

## Purpose

Wait safely for the currently open Codex and Antigravity processes to exit, then invoke the full backup-safe migration launcher once. This allows the user-authorized Task 47 operation to complete without killing the active desktop session or bypassing the migration guard.

## Safety invariants

- The watcher never terminates a process and never passes `-SkipProcessCheck`.
- It waits for `ChatGPT`, `codex`, `codex-code-mode-host`, `codex-command-runner-*`, `Antigravity`, and `Antigravity IDE` to be absent for a stability interval before invoking the launcher.
- The child launcher creates timestamped backups, updates only structural project/workspace metadata, and performs rollback on failure.
- The watcher writes its progress log under the user temp directory, not into authentication or conversation stores; the migration manifest records the selected SQLite executable and hash.

## Open Threads / Resume Context

- **Status:** completed_without_watcher
- **Intent:** The guarded live migration completed without needing a second watcher run; the watcher remains available only as a fail-closed future utility.
- **Next steps:** Do not start it for this task. Inspect the existing migration log/manifest and perform the normal desktop UI spot-check when convenient.
- **Blockers / notes:** No process was terminated and no process guard was bypassed. Remaining open items are real WPF/device and third-party-viewer checks.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Added a non-destructive wait-and-launch path for the real post-exit migration. | Codex |
