# OpenNotes.csproj
> Last updated: 2026-08-24（5.0.0 release metadata） | Protection: STANDARD

## Purpose

The build definition for the OpenNotes desktop application. The file name, assembly name, solution display name, release workflow and installer executable are OpenNotes; the `Caelum` root namespace remains only to preserve compiled XAML/type compatibility.

## Important Notes / NEVER Change

- Keep `RootNamespace` as `Caelum` until a separately versioned namespace migration exists.
- Keep the `%LOCALAPPDATA%\Caelum` data directory and `WindowsNotesApp` AppX identity as legacy compatibility identifiers.
- Keep `OpenNotes` as `AssemblyName`, `Product`, and the project filename so new builds and installers use the renamed product.
- Release metadata is `5.0.0`/`5.0.0.0`; retain `RootNamespace=Caelum` for compatibility.
- The test project references this file through `..\OpenNotes.csproj`.

## Open Threads / Resume Context

- **Status:** complete
- The rename and executable-icon verification are complete. `ApplicationIcon` remains pointed at `Assets/app-icon.ico`; the asset now carries native Windows shell sizes through 256×256.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-24 | Bumped assembly/package metadata to OpenNotes 5.0.0 for the release. | Codex |
| 2026-08-23 | Verified the unchanged `ApplicationIcon` binding against the rebuilt high-resolution multi-frame ICO. | Codex |
| 2026-08-20 | Renamed the project file and assembly-facing build identity from Caelum to OpenNotes while retaining legacy namespace/data compatibility. | Codex |
