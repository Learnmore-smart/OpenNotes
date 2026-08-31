# OpenNotes.csproj
> Last updated: 2026-08-24 | Protection: STANDARD

## Purpose

Defines the .NET 8 WPF desktop build, assembly identity, release version, runtime and package dependencies.

## Important Notes / NEVER Change

- Preserve the `Caelum` root namespace and `WindowsNotesApp` compatibility identity.
- Release builds remain Windows x64 self-contained when published by the release workflow.

## Open Threads / Resume Context

- **Status:** released (5.2.7 page-rotation drawing hotfix)
- Project, assembly, file and informational versions are aligned to `5.2.7` / `5.2.7.0`; the `Caelum` root namespace and Windows x64 self-contained release settings remain unchanged. Tag `v5.2.7` published both verified GitHub assets.
- **Status:** ready_for_release (5.2.1 large-PDF crash hotfix)
- Project, assembly, file and informational versions are aligned to `5.2.1`;
  the `Caelum` namespace and compatibility identities remain unchanged.
- **Status:** ready_for_release (5.2.0 feature release metadata)
- Project, assembly, file and informational versions are aligned to `5.2.0`; the `Caelum` namespace and all compatibility identities remain unchanged.
- **Status:** complete (5.1.2 Lucide icon patch metadata)
- Project, assembly, file and informational versions are aligned to `5.1.2`; all compatibility identities remain unchanged.
- **Status:** complete
- Project, assembly, file and informational versions are aligned to `5.0.1` for the frozen-`ScaleTransform` startup-crash patch; the existing `v5.0.0` tag remains immutable.
