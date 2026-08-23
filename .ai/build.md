# build.ps1
> Last updated: 2026-08-23 | Protection: STANDARD

## Purpose

Builds the Windows desktop application. Local installer packaging follows the release workflow's explicit `dotnet publish` plus Inno Setup `ISCC.exe` commands; this script does not package by itself.

## Open Threads / Resume Context

- **Status:** complete
- The local-only package was created with a clean self-contained win-x64 publish followed by Inno Setup 6.7.3. No remote API, tag, Pages deployment, or GitHub Release was invoked.

## Important Notes / NEVER Change

- The requested artifact is local installation media only.
- Preserve the existing OpenNotes executable name and Caelum compatibility identities.
