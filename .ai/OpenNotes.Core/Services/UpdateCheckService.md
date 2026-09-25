# OpenNotes.Core/Services/UpdateCheckService.cs

> Last updated: 2026-09-25 (V6: User-Agent reports caller version) | Protection: STANDARD

## Purpose

Check the latest stable OpenNotes GitHub Release without owning UI, process launch, downloads, or installation.

## Public API

- `UpdateCheckService.CheckAsync(Version, CancellationToken)` returns the installed version, normalized latest version, trusted release URI, and availability comparison.
- `UpdateCheckException.Kind` categorizes network, timeout, HTTP status, and invalid-response failures.
- `IsTrustedReleaseUri` is reused by MainWindow immediately before browser launch.

## Important Notes / NEVER Change

- Keep the service UI-free and accept an injected `HttpClient` for deterministic tests.
- Accept only HTTPS `github.com/Learnmore-smart/Windows-Notes/releases/` targets.
- Normalize both versions to four non-negative numeric components; never treat parse/network failure as up to date.
- Preserve caller cancellation; the internal request timeout is 10 seconds.

## Open Threads / Resume Context

- **Status:** complete for service scope
- **Result:** injected HTTP client, 10-second linked timeout, GitHub headers, strict JSON/tag parsing, four-part comparison, trusted release URL validation, and categorized failures are implemented without UI or process-launch ownership.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-25 | V6 Task 10: `SendLatestReleaseRequestAsync` takes the caller's normalized installed version so the `OpenNotes/{version}` User-Agent distinguishes the WPF 5.x and WinUI 6.x channels (previously pinned to `ProductInfo.Version`); `UpdateCheckServiceTests` asserts `OpenNotes/5.2.7.0`. | Devin |
| 2026-09-01 | Implemented the fail-closed GitHub latest-release service; focused tests pass 22/22. | Codex |
| 2026-09-01 | Created before the update-check service implementation. | Codex |
