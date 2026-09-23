# OpenNotes.WinUI/OpenNotes.WinUI.csproj
> Last updated: 2026-09-23 (V6 Task 6 — pdfium native package) | Protection: STANDARD

## Purpose
The V6 WinUI 3 (Windows App SDK) shell project. Unpackaged (`WindowsPackageType=None`), Windows App SDK self-contained (`WindowsAppSDKSelfContained=true`) — no MSIX install, no `Bootstrap.Initialize` call needed (the self-contained WASDK runtime DLLs sit next to the exe). NOTE: `WindowsAppSDKSelfContained` covers only the WASDK runtime — the .NET 8 runtime stays **framework-dependent** until publish adds `--self-contained true` (same pattern as `release.yml` uses for the WPF app).

## What It Does
- `net8.0-windows10.0.19041.0` + `TargetPlatformMinVersion 10.0.17763.0`, `UseWinUI=true`.
- `RootNamespace=Caelum` (compiled-XAML/type compatibility), `AssemblyName=OpenNotes.WinUI` → `OpenNotes.WinUI.exe`.
- x64 only: `Platforms=x64`, `Platform=x64`, `RuntimeIdentifier=win-x64` — `dotnet build` works without `-p:Platform`; the `.sln` maps solution `Any CPU` rows to project `x64`.
- `Nullable`/`ImplicitUsings` disabled (matches `OpenNotes.Core` style).
- Version `6.0.0` / Assembly+File `6.0.0.0`, `Product=OpenNotes`.
- Packages: `Microsoft.WindowsAppSDK 1.8.260804001` (newest stable 1.8.x at scaffold time), `Microsoft.Windows.SDK.BuildTools 10.0.26100.4654`, `Microsoft.Graphics.Win2D 1.4.0` (reserved for ink/annotation rendering per plan), `PdfiumViewer.Native.x86_64.v8-xfa 2018.4.8.256` (Task 6 — drops `x64\pdfium.dll` next to the exe; Core `PdfiumRasterizer` preloads it from `BaseDirectory/x64`).
- `ProjectReference` → `..\OpenNotes.Core\OpenNotes.Core.csproj`.

## Important Notes / NEVER Change
- Keep `RootNamespace=Caelum` until the separately planned namespace migration.
- Keep `WindowsPackageType=None` + `WindowsAppSDKSelfContained=true`; the app must stay a portable unpackaged folder.
- No project may reference BOTH `OpenNotes` (WPF) and `OpenNotes.WinUI` — same-namespace same-name types (`Caelum.App`, `Caelum.MainWindow`, future ported `Caelum.*` types) cause CS0433 ambiguity. `OpenNotes.WinUI.Tests` (when created) must reference WinUI+Core only.
- `build.ps1` hard-codes `.\OpenNotes.csproj` and cannot build this project — invoke `dotnet build OpenNotes.WinUI\OpenNotes.WinUI.csproj` directly.

## Open Threads / Resume Context
- **Status:** GREEN — `dotnet build` 0 errors; editor smoke 60/60 (pdfium rasterization live).
- No `Package.appxmanifest` — not needed for unpackaged builds; add only if a packaged build mode appears.
