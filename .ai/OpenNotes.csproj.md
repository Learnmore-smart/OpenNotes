# OpenNotes.csproj
> 2026-09-02 GREEN: package/assembly/file/informational metadata are 5.2.9/5.2.9.0 for the Edge-PDF compatibility release; `RootNamespace=Caelum`, self-contained win-x64 settings, and all compatibility identities remain unchanged.
> 2026-08-31 GREEN: package/assembly/file/informational metadata are 5.2.8/5.2.8.0 for the eraser stylus-crash hotfix; `RootNamespace=Caelum` and all compatibility identities remain unchanged.
> 2026-08-30 GREEN: package/assembly/file/informational metadata are 5.2.6/5.2.6.0 for the editor reliability, page reorder, editable shape, and detachable-tab release; compatibility identities remain unchanged.
> 2026-08-28 GREEN: package/assembly/file/informational metadata are 5.2.4/5.2.4.0 for the selection/text/ruler regression release; `RootNamespace=Caelum` and all compatibility identities are preserved.
> Last updated: 2026-08-24（5.0.0 release metadata） | Protection: STANDARD

> 2026-08-25 GREEN: package/assembly/file/informational metadata are 5.2.2/5.2.2.0 for the fixed-sidebar and centered-page-navigation patch; `RootNamespace=Caelum` and all storage/package compatibility identities are preserved.

## Purpose

The build definition for the OpenNotes desktop application. The file name, assembly name, solution display name, release workflow and installer executable are OpenNotes; the `Caelum` root namespace remains only to preserve compiled XAML/type compatibility.

## Important Notes / NEVER Change

- Keep `RootNamespace` as `Caelum` until a separately versioned namespace migration exists.
- Keep the `%LOCALAPPDATA%\Caelum` data directory and `WindowsNotesApp` AppX identity as legacy compatibility identifiers.
- Keep `OpenNotes` as `AssemblyName`, `Product`, and the project filename so new builds and installers use the renamed product.
- Release metadata is being advanced to `5.2.3`/`5.2.3.0`; retain `RootNamespace=Caelum` for compatibility.
- The test project references this file through `..\OpenNotes.csproj`.

## Open Threads / Resume Context

- **Status:** complete
- Release metadata is `5.2.3`/`5.2.3.0`; ProductInfoTests proved RED against the stale value before the source metadata update and GREEN afterward. No compatibility identifiers changed.
- **Status:** complete
- The rename and executable-icon verification are complete. `ApplicationIcon` remains pointed at `Assets/app-icon.ico`; the asset now carries native Windows shell sizes through 256×256.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-26 | Bumped assembly/package metadata to 5.2.3/5.2.3.0 while preserving legacy compatibility identifiers; focused ProductInfo tests are GREEN. | Codex |
| 2026-08-24 | Bumped assembly/package metadata to OpenNotes 5.0.0 for the release. | Codex |
| 2026-08-23 | Verified the unchanged `ApplicationIcon` binding against the rebuilt high-resolution multi-frame ICO. | Codex |
| 2026-08-20 | Renamed the project file and assembly-facing build identity from Caelum to OpenNotes while retaining legacy namespace/data compatibility. | Codex |
