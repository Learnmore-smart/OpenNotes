# installer.iss
> 2026-08-28 GREEN: default installer version is 5.2.5; AppId and per-user upgrade behavior are preserved.
> 2026-08-28 GREEN: default installer version is 5.2.4; AppId and per-user upgrade behavior are preserved.

> Last updated: 2026-08-24 | Protection: CRITICAL

## Purpose

Builds the self-contained per-user OpenNotes installer.

## Important Notes / NEVER Change

- Preserve AppId, per-user install semantics and upgrade compatibility.
- Keep the default version aligned with `OpenNotes.csproj` and ProductInfo.

## Current Status

- Default version is `5.2.4`; AppId and per-user upgrade behavior remain unchanged.
- Default version is `5.2.2` for the navigation layout patch; AppId and per-user upgrade behavior remain unchanged.
- Default version is `5.2.1` for the large-PDF crash hotfix; AppId and
  per-user upgrade behavior remain unchanged.
- Default installer version is `5.2.0`; AppId and per-user upgrade behavior are preserved.
- Default installer version is `5.1.2`; output remains a self-contained per-user upgrade-compatible installer.
