# OpenNotes.WinUI/Services/WinUiDialogService.cs
> Last updated: 2026-09-23 (V6 Task 5 — ContentDialog service) | Protection: STANDARD

## Purpose
`public static class WinUiDialogService` — the WinUI replacement for the WPF `DialogService`/`MahApps`-style dialogs used by HomePage/MainWindow flows. Wraps `ContentDialog` with an explicit `XamlRoot` (required for unpackaged/self-contained WinUI).

## API
- `ShowInfoAsync(xamlRoot, title, content)` — single Close button.
- `ShowErrorAsync(xamlRoot, title, content)` — info dialog with the error title; kept separate so call sites mirror WPF semantics.
- `ShowDialogAsync(xamlRoot, title, content, cancelText, confirmText)` → `bool?` — `true` on primary, `false` on secondary, `null` on dismiss (matches the WPF `MessageBoxResult`-style contract used by the update prompt).
- `ShowDangerConfirmAsync(xamlRoot, title, content, cancelText, confirmText)` → `bool?` — same contract; the primary button gets a red style built dynamically from the `ThemeDangerBrush` resource (a shared resource mutation would tint EVERY later dialog).
- `RunUnderDialogGateAsync<T>(Func<Task<T>>)` (internal) — runs a caller-built `ContentDialog`'s `ShowAsync` under the SAME `DialogGate` (HomePage's `PromptForInputAsync`/`PickNotebookTemplateAsync` construct their own dialogs and must serialize with service ones). A stray already-open dialog → `default(T)` — for `ContentDialogResult` that is `None` ("dismissed").

## Important Notes / NEVER Change
- A process-wide `SemaphoreSlim` serializes dialogs: WinUI allows only ONE live `ContentDialog` per `XamlRoot` — a second `ShowAsync` throws `InvalidOperationException`. Keep the semaphore around every show; the private `ShowDialogAsync` ALSO catches `InvalidOperationException` → `null` as defense-in-depth against any dialog that bypassed the gate.
- `xamlRoot` may arrive null (window mid-close): every method no-ops to `false`/`null` in that case — never throw.
- All call-site text flows through `LocalizationService` — this class itself keeps no literal strings.
