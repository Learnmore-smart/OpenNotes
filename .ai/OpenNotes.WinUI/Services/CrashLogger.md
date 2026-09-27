# OpenNotes.WinUI/Services/CrashLogger.cs
> Last updated: 2026-09-26 (T14-A — created) | Protection: STANDARD

## Purpose
Last-resort crash journal (`Caelum.Services.CrashLogger`, internal static). Born from the v6.0.3 field crash — a stowed WinRT `0xC000027B`/`E_INVALIDARG` while inking that left no managed stack. Appends one timestamped entry per event so the next crash ships the stack.

## API
- `Log(string source, Exception exception)` — `AggregateException` is `Flatten()`ed first (UnobservedTaskException arrives aggregated); body is `exception.ToString()` (type + message + full stack + inner chain). The whole format sits inside try/catch — a throwing `ToString()`/`Flatten()` falls back to logging `GetType().Name` (review fix I1: formatting must not kill the journal exactly when it matters).
- `Log(string source, string detail)` — free-form entry for dodged faults that never threw (e.g. `StrokeRenderer` skipping a non-finite outline).

## Behaviour
- Destination: `Path.Combine(ProductInfo.GetDataDirectory(), "logs")` → `crash-yyyyMMdd-HHmmss.log` (`%LOCALAPPDATA%\Caelum\logs`; honours `OPENNOTES_DATA_ROOT`). Entries `AppendAllText` — two faults in the same second share a file.
- `Prune` keeps only the newest `MaxFiles = 20` `crash-*.log` (ordinal name sort = chronological).
- All I/O sits under one `lock` inside try/catch; failures degrade to `Debug.WriteLine`. **NEVER let this throw** — it runs inside crash handlers where a throw is fatal and silent.

## Callers
- `App.xaml.cs` — `Application.UnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`.
- `StrokeRenderer.LogFault` — capped at 3 writes per process (per-pointer-move call rate).

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-26 | Created for T14-A crash journaling. | Devin |
