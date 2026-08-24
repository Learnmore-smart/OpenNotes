# build.ps1
> Last updated: 2026-08-24（5.0.0 release package） | Protection: STANDARD

## Purpose

Builds the Windows desktop application. Local installer packaging follows the release workflow's explicit `dotnet publish` plus Inno Setup `ISCC.exe` commands; this script does not package by itself.

## Open Threads / Resume Context

- **Status:** complete
- The OpenNotes 5.0.0 package was created with a self-contained win-x64 Release publish followed by Inno Setup 6.7.3. Remote Git/tag/Release work is tracked by the release execution handoff; Pages deployment remains out of scope.

## Important Notes / NEVER Change

- The requested artifact is local installation media only.
- Preserve the existing OpenNotes executable name and Caelum compatibility identities.
