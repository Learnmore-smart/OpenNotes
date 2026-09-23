# OpenNotes.Core/OpenNotes.Core.csproj
> Last updated: 2026-09-22（V6 WinUI 3 migration Task 1 — UI-free extraction on `v6/winui3`）| Protection: STANDARD

## Purpose

Build definition for the `OpenNotes.Core` class library: the UI-free services/models extracted from the WPF app so a future WinUI 3 host can share them. `RootNamespace` stays `Caelum` — namespaces in moved files are unchanged (`Caelum.Services` / `Caelum.Models`).

## Key Settings

- `TargetFramework` = `net8.0`（**not** `net8.0-windows`）：no WPF/WinForms/Windows Desktop references allowed in this project.
- `Nullable` = `disable`，`ImplicitUsings` = `disable`：matches the main project; moved files compile identically.
- `AssemblyName` = `OpenNotes.Core`；package：`PdfSharpCore 1.3.67`（needed by `Services/PdfAtomicFile.cs`）。
- `InternalsVisibleTo` → `OpenNotes` + `OpenNotes.Tests` + `OpenNotes.WinUI`：moved files expose internal members consumed by the WPF app (`PdfAtomicFile`, `RecycleBinService`, `WindowsEnvironment`, `UpdateCheckService.IsTrustedReleaseUri`, `PdfSaveCoordinator` counters), by the test suite, and by the V6 WinUI host (`RecycleBinService`/`PdfAtomicFile` are `internal` — without IVT Task 5 HomePage hits CS0122).

## Important Notes / NEVER Change

- Keep `RootNamespace=Caelum`; moved files keep `namespace Caelum.Services` / `Caelum.Models` so serialized types and compiled references stay compatible.
- Do not add WPF/WinForms package or framework references; `System.Drawing`-family types must not appear on public API surfaces.
- The WPF project excludes this folder from its default globs via `Compile/EmbeddedResource/None/Page Remove="OpenNotes.Core\**"` in `OpenNotes.csproj`.

## Change History
| Date | Change | Author |
|---|---|---|
| 2026-09-22 | Created for Task 1 of the V6 WinUI 3 migration: 19 Services + 7 Models files moved from the app project unchanged. | Devin |
| 2026-09-22 | Added `InternalsVisibleTo` → `OpenNotes.WinUI` so the V6 host can consume internal services (review follow-up). | Devin |
