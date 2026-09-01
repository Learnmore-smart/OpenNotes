# 2026-08-31 Check for Updates Design

> Last updated: 2026-08-31 | Protection: STANDARD

## Purpose

Mirror the approved design for a user-triggered GitHub Release update check from the MainWindow More menu.

## Open Threads / Resume Context

- **Status:** implemented_and_verified
- **Intent:** Add a localized `Check for updates` command between Settings and About, backed by a testable GitHub Releases client and explicit result/error dialogs.
- **Result:** Manual update checking, strict GitHub release parsing, localized MainWindow UI, trusted browser launch, and failure/cancellation handling are implemented. Final evidence: focused 33/33, full 399/399, Release build 0 errors, i18n 304 catalog entries / 494 calls / 0 hard-coded visible strings, and clean diff check.
- **Constraints:** No startup/background checks, automatic downloads, silent installation, prerelease adoption, or changes to release/version metadata.

## Agent Decisions / Thoughts

- **2026-08-31 Codex:** Use GitHub's latest-release API instead of merely opening the releases page, so the application can distinguish newer/current versions while keeping installation user-controlled.
- **2026-08-31 Codex:** Keep network/version logic in a dedicated service; MainWindow owns only command state and localized presentation. Normalize both sides to four numeric components so tags such as `5.2.7` compare equal to assembly version `5.2.7.0`, and bound the request to 10 seconds.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-31 | Created the mirror for the approved update-check design. | Codex |
