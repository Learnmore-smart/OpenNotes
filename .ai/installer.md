# installer.iss
> Last updated: 2026-08-23 | Protection: STANDARD

## Purpose

Defines the Inno Setup package for the OpenNotes Windows desktop application.

## Open Threads / Resume Context

- **Status:** complete
- The installer consumes `Assets/app-icon.ico`. A clean Release publish produced `installer_output/OpenNotes-Setup-4.0.0-local.exe` (53.19 MiB; SHA-256 `F9BFA878428346186371379D6E2C6B0BFC564A6421B5227BEF15670D1AD4DBAF`). A temporary silent install/uninstall passed with no `Caelum.*` payload files. The installer is intentionally unsigned and local-only.

## Important Notes / NEVER Change

- Keep upgrade/application identifiers stable so existing installations are not stranded.
- Do not publish the generated installer without an explicit later request.
