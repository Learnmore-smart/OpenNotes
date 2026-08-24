# app-icon.ico

> Last updated: 2026-08-23 | Protection: STANDARD

## Purpose

Multi-resolution Windows executable icon built from the current OpenNotes favicon artwork.

## What It Does

Supplies the icon embedded into `OpenNotes.exe` through `OpenNotes.csproj` and copied beside build outputs.

## Dependencies

- **Source artwork:** `public/logo/OpenNotes_favicon/web-app-manifest-512x512.png` — current 512×512 OpenNotes mark.
- **Consumer:** `OpenNotes.csproj` — references this file through `ApplicationIcon`.

## Open Threads / Resume Context

- **Status:** complete
- The executable icon now contains 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel 32-bit frames. No follow-up is open.

## Agent Decisions / Thoughts

- **2026-08-23 Codex:** Reuse the existing 512×512 brand source instead of inventing or AI-redrawing the logo; the blur is caused by missing large ICO frames, not inadequate source artwork.

## Important Notes / NEVER Change

- Preserve the current white rounded-square OpenNotes artwork and blue/cyan palette unless a separate branding change is requested.
- Keep small native frames as well as 256×256 so Windows does not have to downscale one large bitmap for every shell context.

## Bug Fixes

| Date | Bug | Cause | Fix |
|---|---|---|---|
| 2026-08-23 | Executable icon appears blurred at larger shell sizes. | ICO contained only 16×16, 32×32, and 48×48 frames. | Rebuilt from the 512×512 source with nine 32-bit frames from 16×16 through 256×256. |

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Rebuilt the executable icon with native Windows shell sizes through 256×256. | Codex |
