# OpenNotes.WinUI/Controls/PageTemplatePickerDialog.cs
> Last updated: 2026-09-26 (T12-C — Fluent card radii 18→10 / preview 16→8) | Protection: STANDARD

## Purpose
`public sealed class PageTemplatePickerDialog : ContentDialog` (`Caelum.Controls`) — the WinUI port of the WPF `PageTemplatePickerWindow`. A 3×3 card grid where each card carries a code-drawn mini preview sketch (`BuildPreview`/`BuildStaffLines`/`MakeLine`), a localized title and a localized hint.

## Two modes (WPF parity)
- **Insert-page mode** — parameterless ctor. A card click selects that template AND closes the dialog (`IsConfirmed = true`, `SelectedTemplate` set) — the WPF picker treated a card click as `DialogResult = true`. Cancel/Esc/dismiss leaves `IsConfirmed = false`. Used by `EditorPage.InsertPageAtAsync`.
- **Notebook-creation mode** — `PageTemplatePickerDialog(string initialFolderPath)`. A card click only selects; the primary "Create" button confirms, and `IsPrimaryButtonEnabled` stays `false` until `SelectedFolderPath` is non-empty. The folder row (label + ellipsized path + `FolderOpen` browse button → `FolderPicker` initialized on `MainWindow`'s hwnd) is only visible in this mode. Used by `HomePage.PickNotebookTemplateAsync`.

## Callers / contract
- `SelectedTemplate` (last/confirmed card), `SelectedFolderPath` (notebook mode), `IsConfirmed` (WPF `DialogResult == true` parity).
- Callers MUST set `XamlRoot` and show via `WinUiDialogService.RunUnderDialogGateAsync` — the dialog itself does not take the gate.
- `TemplateOptions` lists all nine `PageInsertTemplate` values in WPF card order with their title/hint localization keys; card AutomationIds mirror the WPF `x:Name`s (`BlankCard`, `NotebookCard`, …).
- Insert mode localizes with the WPF catalog pair `Editor.InsertPageDialogTitle`/`Editor.InsertPageDialogSubtitle` — the earlier `Editor.PageTemplateTitle`/`Subtitle` names never existed and `Get()` threw `KeyNotFoundException` inside the ctor's `ApplyLocalization()` (before any caller try/catch → crashed the async-void insert click). `WinUiDialogsServicesSourceTests` pins the real keys + bans the old names.
- `LocalizationService.LanguageChanged` → `ApplyLocalization()` (title/subtitle/cards/folder/buttons); `WinUiThemeService.ThemeApplied` → `Content = BuildContent()` rebuild (theme brushes are baked into code-built content; selection + folder are field-backed and survive the rebuild). Subscriptions attach in `Opened` (a `ShowAsync` that throws before opening never raises `Closed`, so ctor-time subscription would leak) and detach in `Closed`; `OnLanguageChanged`/`OnThemeApplied` additionally guard on `_opened`. The `Opened` hook is idempotent (re-show cannot double-subscribe).
- `BrowsePathButton_Click` is async-void by necessity; it has a last-resort `catch` (no App.UnhandledException backstop).

## Important Notes / NEVER Change
- Do not add a second dialog semaphore here — gate entry is the caller's job (`RunUnderDialogGateAsync`).
- Keep `IsConfirmed` the only "did the user pick" signal; `ShowAsync`'s `ContentDialogResult` is deliberately not interpreted at call sites (card-click confirm → `Hide()`, NOT `ContentDialogResult.Primary`).

## Open Threads / Resume Context
- **Status:** GREEN — builds 0 err/0 warn; source-pinned by `WinUiDialogsServicesSourceTests.PageTemplatePickerCoversBothModesBehindTheGate`.

## Change History
- 2026-09-26 T12-C: card `CornerRadius` 18→10 (ThemeRadiusCard) and preview well 16→8 — the WPF rounds read soft next to the T12 radius-token surfaces; purely cosmetic, both modes unchanged. | Devin
- 2026-09-25 Task 9 Phase B quality pass: real WPF insert-mode keys (`Editor.InsertPageDialogTitle`/`Subtitle`) replace the nonexistent `Editor.PageTemplateTitle`/`Subtitle` (ctor-path `KeyNotFoundException` → async-void crash); language/theme subscriptions deferred ctor→`Opened` with `_opened` guards (a `ShowAsync` throw must not leak handlers). | Devin
- 2026-09-25 Task 9 Phase B: initial WinUI port (WPF `PageTemplatePickerWindow` parity); replaced HomePage's compact radio-card stand-in. | Devin
