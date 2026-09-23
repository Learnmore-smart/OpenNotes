# OpenNotes.csproj
> 2026-09-22 V6 Task 4 Step 1: added `Compile/EmbeddedResource/None/Page/ApplicationDefinition Remove="OpenNotes.WinUI\**"` so the sibling WinUI 3 project (and its generated `obj` items) stays out of the WPF project's default globs. Release `dotnet build` remains 0 errors.
> 2026-09-22 V6: added `ProjectReference` to `OpenNotes.Core\OpenNotes.Core.csproj` and `Compile/EmbeddedResource/None/Page Remove="OpenNotes.Core\**"` so the sibling Core folder is excluded from this project's default globs (the WPF app consumes moved `Caelum.Services`/`Caelum.Models` types through the reference; `InternalsVisibleTo` in Core keeps moved internals reachable). Part of the `v6/winui3` Task 1 extraction.
> 2026-09-22 GREEN: package/assembly/file/informational metadata are 5.2.15/5.2.15.0 for the sidebar content-offset fix; `RootNamespace=Caelum`, self-contained win-x64 settings, and all compatibility identities remain unchanged.
> 2026-09-02 RELEASED: package/assembly/file/informational metadata are 5.2.9/5.2.9.0 in tag `v5.2.9`; `RootNamespace=Caelum`, self-contained win-x64 settings, and all compatibility identities remain unchanged.
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
| 2026-09-22 | V6 WinUI 3 Task 4 Step 1 (`v6/winui3`): added `OpenNotes.WinUI\**` item exclusions (Compile/EmbeddedResource/None/Page/ApplicationDefinition) so the new WinUI 3 project's sources and `obj` output are never globbed by the WPF build. | Devin |
| 2026-09-22 | V6 WinUI 3 Task 1 (`v6/winui3`): added `ProjectReference` to `OpenNotes.Core` and `OpenNotes.Core\**` item exclusions so the extracted sibling project builds separately while the app keeps using the moved `Caelum.*` types. | Devin |
| 2026-08-26 | Bumped assembly/package metadata to 5.2.3/5.2.3.0 while preserving legacy compatibility identifiers; focused ProductInfo tests are GREEN. | Codex |
| 2026-08-24 | Bumped assembly/package metadata to OpenNotes 5.0.0 for the release. | Codex |
| 2026-08-23 | Verified the unchanged `ApplicationIcon` binding against the rebuilt high-resolution multi-frame ICO. | Codex |
| 2026-08-20 | Renamed the project file and assembly-facing build identity from Caelum to OpenNotes while retaining legacy namespace/data compatibility. | Codex |
