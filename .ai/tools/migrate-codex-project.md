# tools/migrate-codex-project.ps1
> Last updated: 2026-08-20（OpenNotes 项目路径迁移脚本） | Protection: CRITICAL

## Purpose

Compatibility entry point for the full Codex/Antigravity project-and-session metadata migration from both historical Caelum roots to the OpenNotes checkout while preserving the existing canonical project ID and single compatibility project name `Caelum`.

## Safety invariants

- The wrapper delegates all writes to `Migrate-AssistantConversations.ps1`, whose guard also covers Antigravity, `Antigravity IDE`, and `codex-command-runner-*`.
- It never reads or writes `auth.json`, token files, logs, attachments, or conversation-body rows.
- The main script backs up SQLite main files plus `-wal`/`-shm` companions, JSON/session metadata, and SHA-256 manifests before mutation.
- The main script derives the canonical project ID from the existing OpenNotes `project_roots` record when no explicit ID is supplied, then updates only structural association metadata in transactions.
- A post-migration outer rollback restores every backed-up source, including absent/present sidecar state, if any later validation fails.
- Session body directories and the session index are structurally protected; rollout body bytes and conversation IDs are checked by the main fixture.

## Open Threads / Resume Context

- **Status:** completed
- The guarded live migration has already run after the desktop processes were quiescent; the physical checkout is `D:\Noah\文档\Coding\1. Open-Source\OpenNotes`.
- The before/after backup manifest is retained as Task 47 evidence. A read-only state snapshot confirms one `Caelum` project rooted at OpenNotes and zero old project roots; desktop UI continuation should be spot-checked after a normal app restart.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Added a fail-closed, backed-up, transactional project/path migration with thread-association fingerprints. | Codex |
