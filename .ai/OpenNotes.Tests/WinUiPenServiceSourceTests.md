# OpenNotes.Tests/WinUiPenServiceSourceTests.cs
> Last updated: 2026-09-25 | Protection: STANDARD

## Purpose

Source-contract fixture pinning the window-scoped `PenService` wiring
(final-review fix for the HWND fan-out):

- `PenServiceIsOwnedOnceByMainWindowAndRoutedToTheActiveEditor` —
  `MainWindow.xaml.cs` holds the only `new PenService()` site, subscribes
  `ToolToggleRequested`/`PenDeviceDetected`, calls `_penService.Initialize(this)`,
  routes both events through `_activeTab?.Frame?.Content as EditorPage` →
  `HandlePenToolToggle`/`HandlePenDeviceDetected`, and disposes the service
  inside `MainWindow_Closed`.
- `EditorPageConsumesTheSharedServiceAndGatesTheCallbacks` —
  `EditorPage.xaml.cs` pulls `GetMainWindow()?.GetOrCreatePenService()`,
  never constructs/initializes/subscribes/disposes its own, keeps
  `!_isHostActive || _resourcesReleased` gates before enqueue AND inside
  the toggle callback, and still pushes the shared service to every ink
  surface via `PushPenServiceToPages`/`SetPenService`.

## Open Threads / Resume Context

- Pure text contracts (`Read`/`ProjectRoot` helper locates
  `OpenNotes.WinUI/`); mirrors `WinUiParitySourceTests` conventions.
