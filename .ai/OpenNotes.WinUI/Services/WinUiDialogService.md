# OpenNotes.WinUI/Services/WinUiDialogService.cs
> Last updated: 2026-09-25 (V6 Task 9 Phase B — z-order no-port note) | Protection: STANDARD

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
- **Popup z-order (T9-B assessment — WPF `PopupZOrderHelper` deliberately NOT ported):** WPF popups are HWND-backed, so `PopupZOrderHelper` called `SetWindowPos(HWND_NOTOPMOST)` on each ContextMenu/ComboBox-dropdown hwnd (pulls it out of the topmost band - stays above the owner, stops floating over other apps after Alt-Tab) and added `WS_EX_NOACTIVATE` so popups never steal activation. WinUI `MenuFlyout`/`Flyout`/`ContentDialog` are XamlRoot-scoped visuals composited inside the app's swap chain — they can't be clipped by sibling HWNDs and expose no HWND to reorder, so showing on the current XamlRoot IS the topmost guarantee. The class doc comment records this mapping; no code crosses over.
