# OpenNotes.WinUI/MainWindow.xaml
> Last updated: 2026-09-23 (V6 Task 4 Steps 2–5 — themed chrome + tab strip; caption fix `SetBorderAndTitleBar`) | Protection: STANDARD

## Purpose
WinUI 3 `Microsoft.UI.Xaml.Window` markup (NOT WPF `System.Windows.Window`) — the V6 chrome: 40px custom title bar, tab strip, per-tab `Frame` host area.

## Layout
- `RootGrid` (bg `ThemeWindowBrush`): Row0 = `TitleBarGrid` (`ThemeToolbarBrush`), Row1 = tab strip `Border` (`ThemePaperBrush` + bottom `ThemeBorderBrush`), Row2 = `TabContentArea` (frames added in code).
- **Title bar:** `AppTitleBar` (the `SetTitleBar` drag surface) contains `BrandMarginRail` + product name + nav Back/Forward/Home buttons — mirroring the WPF flow; framework passthrough keeps interactive children clickable over the caption rect. Right-aligned overlay `StackPanel` holds Minimize/Maximize/Close caption buttons.
- **Tab strip:** horizontal `ListView` (`TabStrip`, `ItemsStackPanel` horizontal, internal scroll disabled) inside an outer `ScrollViewer` + `NewTabButton` — same structure as the WPF `TabBar` + button. Drag-reorder needs ALL of `CanReorderItems`+`CanDragItems`+`AllowDrop` (`CanReorderItems` alone is inert — earlier builds could not reorder); `IsItemClickEnabled` + single selection drives activation; `ItemsSource` binds `OneTime` (the `ObservableCollection` itself carries change notifications).
- Styles ported to VSM form: `NavButtonStyle` (32×28 pill, radius 16), `TitleBarButtonStyle` (46×40), `CloseButtonStyle` (red hover #C42B1C / pressed #B22A1B, `Content.Foreground`→White), `TabListViewItemStyle` (bare transparent container — the pill paints all chrome), `TabItemTemplate` (`x:DataType AppTab`: icon glyph + truncated title + close `Button`, binds the model's computed chrome properties, hover via code-behind `PointerEntered/Exited`).

## Important Notes / NEVER Change
- **Caption fix (defect found in spec review of `dbfd81a`):** `ExtendsContentIntoTitleBar=true` alone does NOT remove the system Min/Max/Close — they render TOPMOST over the right ~138px, exactly over this file's right-aligned caption `StackPanel`, and swallow real clicks (UIA Invoke passes anyway because it bypasses hit-testing). The custom buttons below are only the true caption UI because code-behind calls `OverlappedPresenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false)` in `MainWindow.xaml.cs` `ApplyCustomChrome` (re-applied on `AppWindow.Changed`/`DidPresenterChange`). NEVER remove that call when editing chrome — if a future chrome rewrite recreates/swaps the presenter, re-apply there too.
- WinUI `Window` has NO `Resources` property — all styles/templates live under `RootGrid.Resources`.
- `{ThemeResource}` is used everywhere (not `StaticResource`) so `WinUiThemeService` resource replacement re-renders chrome live.
- Lucide vectors are NOT ported yet: `FontIcon` glyphs stand in (E72B/E72A back/fwd, E80F home, E921/E922/E923 min/max/restore, E8BB close, E710 plus, E711 tab-close, E8A5 doc) — swap when the icon port lands.
- The only WPF `AutomationProperties.AutomationId` on MainWindow was `MoreButton` (deferred to Task 5); stable IDs were added to all new chrome for the UIA smoke scripts — keep them.
- The nav/brand `StackPanel` inside `AppTitleBar` carries `Margin="0,0,138,0"` (≥3×46px caption cluster) and the presenter sets `PreferredMinimumWidth/Height` (560×360) — without both, narrow windows slide nav/brand under the caption buttons.
- `x:Bind` is `OneWay` — it keeps no `BindingExpression`, so `PointerExited` must `SetValue` the computed `TabBackground` (NOT `ClearValue`, which would null the pill and strip the active tab's surface brush — regression fixed).
- No app icon `Image` in the title bar yet: `Assets/app-icon.ico` is ICO (WinUI `Image` can't decode it); the icon port should add a PNG.

## Open Threads / Resume Context
- **Status:** verified — real-pointer UIA smoke clicks through new-tab, tab select, middle-close, min/max/restore/close, NavHome.
