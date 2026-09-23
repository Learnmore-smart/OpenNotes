# OpenNotes.WinUI/Pages/HomeTileTemplateSelector.cs
> Last updated: 2026-09-23 (V6 Task 5 — tile template selector) | Protection: STANDARD

## Purpose
`Caelum.Pages.HomeTileTemplateSelector : DataTemplateSelector` — picks `AddTileTemplate` / `FolderTileTemplate` / `FileTileTemplate` by `HomeTile.IsAddTile`/`IsFolder` (files are the fallthrough). The direct port of the WPF `HomeTileTemplateSelector` used by the home `ItemsRepeater` (`TilesRepeater`).

## Important Notes
- Exposed template properties are plain `DataTemplate` get/set — wired in `HomePage.xaml` to the `x:DataType="models:HomeTile"` templates.
- Returns `base.SelectTemplateCore` for null/unrecognized items so an unexpected entry never crashes the list.
