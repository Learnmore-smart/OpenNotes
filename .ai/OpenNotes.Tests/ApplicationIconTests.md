# ApplicationIconTests.cs

> Last updated: 2026-08-23 | Protection: STANDARD

## Purpose

Regression contract for the Windows executable icon's embedded native resolutions.

## What It Does

Reads the real `Assets/app-icon.ico` directory and verifies that standard shell sizes through 256×256 are present.

## Open Threads / Resume Context

- **Status:** complete
- RED reported only 16, 32, and 48 pixel frames; GREEN passes with every required size through 256 pixels. No follow-up is open.

## Agent Decisions / Thoughts

- **2026-08-23 Codex:** Test the checked-in production ICO directly so the contract detects future branding bundles that contain only favicon-scale frames.

## Important Notes / NEVER Change

- Do not make the test depend on a generated build-output copy; `Assets/app-icon.ico` is the source of truth.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added the production-ICO frame-table regression contract and completed RED/GREEN verification. | Codex |
