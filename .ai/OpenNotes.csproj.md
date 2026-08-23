# OpenNotes.csproj
> Last updated: 2026-08-20（正式项目改名） | Protection: STANDARD

## Purpose

The build definition for the OpenNotes desktop application. The file name, assembly name, solution display name, release workflow and installer executable are OpenNotes; the `Caelum` root namespace remains only to preserve compiled XAML/type compatibility.

## Important Notes / NEVER Change

- Keep `RootNamespace` as `Caelum` until a separately versioned namespace migration exists.
- Keep the `%LOCALAPPDATA%\Caelum` data directory and `WindowsNotesApp` AppX identity as legacy compatibility identifiers.
- Keep `OpenNotes` as `AssemblyName`, `Product`, and the project filename so new builds and installers use the renamed product.
- The test project references this file through `..\OpenNotes.csproj`.

## Open Threads / Resume Context

- **Status:** in_progress
- The rename verification is complete. Current pass verifies that `ApplicationIcon` continues to point at stable `Assets/app-icon.ico` after that resource is replaced from the user-provided favicon bundle.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Renamed the project file and assembly-facing build identity from Caelum to OpenNotes while retaining legacy namespace/data compatibility. | Codex |
