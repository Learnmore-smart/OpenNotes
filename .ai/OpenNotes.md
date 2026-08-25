# OpenNotes.csproj
> Last updated: 2026-08-24 | Protection: STANDARD

## Purpose

Defines the .NET 8 WPF desktop build, assembly identity, release version, runtime and package dependencies.

## Important Notes / NEVER Change

- Preserve the `Caelum` root namespace and `WindowsNotesApp` compatibility identity.
- Release builds remain Windows x64 self-contained when published by the release workflow.

## Open Threads / Resume Context

- **Status:** complete
- Project, assembly, file and informational versions are aligned to `5.0.1` for the frozen-`ScaleTransform` startup-crash patch; the existing `v5.0.0` tag remains immutable.
