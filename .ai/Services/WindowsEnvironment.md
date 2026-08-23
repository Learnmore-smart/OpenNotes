# WindowsEnvironment
> Last updated: 2026-08-21 | Protection: STANDARD

## Purpose

Provide the WPF process with the Windows directory alias it expects when a host exposes `SystemRoot` but omits `WINDIR`.

## What It Does

`NormalizeForWpf` is called from the `App` type initializer, before the first `Window` is created. It returns without changing anything when `WINDIR` is already populated. If it is missing, it copies a valid, existing `SystemRoot` directory into the current process environment only. It never writes user-level or machine-level environment settings.

## Public API

| Name | Type | Description |
|---|---|---|
| `NormalizeForWpf` | `internal static void` | Restores the process-local `WINDIR` alias from a valid `SystemRoot` when needed. |

## Dependencies

- **Framework:** `System.Environment`, `System.IO.Directory`
- **Internal:** `App.xaml.cs` — invokes the normalization before WPF window initialization.
- **Tests:** `OpenNotes.Tests/WindowsEnvironmentTests.cs` — verifies recovery when `WINDIR` is absent.

## Open Threads / Resume Context

- **Status:** ready_for_next
- **Intent:** Keep the workaround process-local and limited to hosts with a missing `WINDIR` alias.
- **Next steps:** None unless another nonstandard WPF host environment is observed.

## Agent Decisions / Thoughts

- **2026-08-21:** Use `SystemRoot` only when it points to an existing directory. This avoids masking a malformed host environment and avoids system-wide changes.
- **2026-08-21:** Put the call in the `App` type initializer because WPF font initialization can fail while the first `Window` type is being initialized, before `OnStartup` reaches application code.

## Important Notes / NEVER Change

- Do not write `EnvironmentVariableTarget.User` or `EnvironmentVariableTarget.Machine`.
- Do not modify the Windows Fonts registry or remove font files as part of this compatibility path.

## Bug Fixes

| Date | Bug | Cause | Fix |
|---|---|---|---|
| 2026-08-21 | WPF could fail before showing the main window in hosts without `WINDIR` | WPF font bootstrap received an invalid Windows-font URI | Restore `WINDIR` from an existing `SystemRoot` in the process before the first `Window`. |

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Added process-local WPF environment normalization and regression coverage. | Codex |
