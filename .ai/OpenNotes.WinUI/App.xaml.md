# OpenNotes.WinUI/App.xaml
> Last updated: 2026-09-22 (V6 Task 4 Step 1) | Protection: STANDARD

## Purpose
WinUI 3 `Microsoft.UI.Xaml.Application` markup — currently only merges `XamlControlsResources` (`using:Microsoft.UI.Xaml.Controls`) so the empty shell gets Fluent control themes. Theme token port (`Theme*Brush` ThemeDictionaries) is Task 4 Step 3.

## Important Notes / NEVER Change
- Keep `x:Class="Caelum.App"` (RootNamespace compatibility).
