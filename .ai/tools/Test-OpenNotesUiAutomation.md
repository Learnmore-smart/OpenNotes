# Test-OpenNotesUiAutomation.ps1

## Purpose

Runs a real WPF desktop smoke test against the built OpenNotes executable using Windows UI Automation. It is intentionally narrower than the full Task 48 regression: it proves the visible app window, the More menu, the Settings dialog, language preview refresh, theme preview selection, and Cancel cleanup. Passing `-SaveAndReopen` additionally commits the language/theme choices to the isolated sidecar, restarts OpenNotes, and verifies both selections through the reopened Settings dialog.

## Isolation and safety

- Launches the executable with unique temporary `LOCALAPPDATA`, `APPDATA`, and explicit `OPENNOTES_DATA_ROOT` environment values. The last variable is an opt-in test-only override; production still resolves `%LOCALAPPDATA%\Caelum`.
- Removes `WINDIR` while preserving `SystemRoot` to exercise the WPF startup fallback.
- Selects Français and the dark theme only as in-memory previews, then invokes Cancel.
- With `-SaveAndReopen`, selects Français and the dark theme, invokes Save, restarts the owned process, and verifies the persisted selections before closing the reopened dialog with Cancel.
- Closes the owned process and removes only the exact temporary directory in `finally`.
- Does not open a PDF, modify the user's library, or touch Codex AppData. The persistence variant writes only to its unique temporary sidecar.

## Verified result (2026-08-21)

`powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-OpenNotesUiAutomation.ps1` passed with:

- visible `OpenNotes` main window;
- `MoreButton` and `SettingsMenuItem` discovered and invoked;
- language preview `Français`, with the dialog changing from `设置` / `取消` to `Paramètres` / `Annuler`;
- dark theme preview `◐  Sombre`;
- Cancel closing the settings window and clean process exit.

The full drawing, text-box, cross-page, device, third-party PDF, and visual contrast regressions remain separate manual checks under Task 48.

## Persistence result (2026-08-22)

`powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-OpenNotesUiAutomation.ps1 -SaveAndReopen` passed with the same isolated environment and verified the reopened dialog retained `Français` and `◐  Sombre` after a real Save and process restart.
