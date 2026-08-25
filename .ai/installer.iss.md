# installer.iss

> Last updated: 2026-08-24 | Protection: CRITICAL

## Purpose

Builds the self-contained per-user OpenNotes installer.

## Important Notes / NEVER Change

- Preserve AppId, per-user install semantics and upgrade compatibility.
- Keep the default version aligned with `OpenNotes.csproj` and ProductInfo.

## Current Status

- Default installer version is `5.2.0`; AppId and per-user upgrade behavior are preserved.
- Default installer version is `5.1.2`; output remains a self-contained per-user upgrade-compatible installer.
