# OpenNotes.WinUI/OpenNotes.WinUI.csproj
> Last updated: 2026-09-22 (V6 Task 4 Step 1 — empty window builds and launches) | Protection: STANDARD

## Purpose
The V6 WinUI 3 (Windows App SDK) shell project. Unpackaged (`WindowsPackageType=None`), Windows App SDK self-contained (`WindowsAppSDKSelfContained=true`) — ships as a single .exe folder like the WPF app, no MSIX install, no `Bootstrap.Initialize` call needed (the self-contained runtime DLLs sit next to the exe).

## What It Does
- `net8.0-windows10.0.19041.0` + `TargetPlatformMinVersion 10.0.17763.0`, `UseWinUI=true`.
- `RootNamespace=Caelum` (compiled-XAML/type compatibility), `AssemblyName=OpenNotes.WinUI` → `OpenNotes.WinUI.exe`.
- x64 only: `Platforms=x64`, `Platform=x64`, `RuntimeIdentifier=win-x64` — `dotnet build` works without `-p:Platform`; the `.sln` maps solution `Any CPU` rows to project `x64`.
- `Nullable`/`ImplicitUsings` disabled (matches `OpenNotes.Core` style).
- Version `6.0.0` / Assembly+File `6.0.0.0`, `Product=OpenNotes`.
- Packages: `Microsoft.WindowsAppSDK 1.8.260804001` (newest stable 1.8.x at scaffold time), `Microsoft.Windows.SDK.BuildTools 10.0.26100.4654`, `Microsoft.Graphics.Win2D 1.4.0` (unused by the empty window; reserved for ink/annotation rendering per plan).
- `ProjectReference` → `..\OpenNotes.Core\OpenNotes.Core.csproj`.

## Important Notes / NEVER Change
- Keep `RootNamespace=Caelum` until the separately planned namespace migration.
- Keep `WindowsPackageType=None` + `WindowsAppSDKSelfContained=true`; the app must stay a portable unpackaged folder.

## Open Threads / Resume Context
- **Status:** GREEN — `dotnet build` 0 errors; `OpenNotes.WinUI.exe` launch-smoke stayed alive 8s+ and was killed manually.
- No `Package.appxmanifest` — not needed for unpackaged builds; add only if a packaged build mode appears.
