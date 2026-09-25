# OpenNotes.Tests/RecentColorsTests.cs
> Last updated: 2026-09-25 | Protection: STANDARD

## Purpose

Behavioural fixture for the new UI-free `Caelum.Services.RecentColors`
helper (G4 — the WPF `RecordRecentColor`/`TryParseRecentColor` logic
moved to `OpenNotes.Core` so both shells share it and it can be tested
headlessly):

- `Record_InsertsNewestFirst` — a pick lands at index 0, pushing prior
  entries down.
- `Record_DedupesCaseInsensitively` — re-picking `"#ff8800"` removes the
  old `"#FF8800"` entry instead of duplicating it.
- `Record_CapsAtMaxRecentColors` — a 10-pick flood trims the tail to
  `RecentColors.MaxRecentColors` (8).
- `Record_IsNullAndBlankSafe` — null list / null / empty / whitespace hex
  are no-ops, never throw.
- `TryParse_ReadsRecordedRRGGBB` — `"#RRGGBB"` (the recorded format)
  parses with `a = 255`.
- `TryParse_ToleratesHandEditedAARRGGBB` — `"#AARRGGBB"` (hand-edited
  settings.json) reads the leading alpha.
- `TryParse_RejectsMalformedEntriesWithoutThrowing` — bad length,
  non-hex, missing `#`, empty string all return false without throwing.
- `AppSettings_ExposesTheThreeRecentColorLists` — `RecentPenColors` /
  `RecentHighlighterColors` / `RecentTextColors` exist on `AppSettings`;
  no `RecentShapeColors` (shape colours stay session-only — WPF parity).

## Open Threads / Resume Context

- Pure Core tests, no UI references — the helper deliberately carries no
  `Windows.UI.Color`/`System.Windows.Media` dependency; colour→hex
  formatting stays in each shell.
