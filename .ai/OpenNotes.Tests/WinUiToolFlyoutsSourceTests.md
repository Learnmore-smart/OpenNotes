# OpenNotes.Tests/WinUiToolFlyoutsSourceTests.cs
> Last updated: 2026-09-25 | Protection: STANDARD

## Purpose

Source-contract fixture pinning the G2/G3/G4 port in
`OpenNotes.WinUI/Pages/EditorPage.xaml.cs`:

- `PenFlyoutPortsTheWpfPenPopupSurface` — `ShowPenFlyout` exists with the
  WPF `_penPopup` surface: `Editor.Pen.Size` slider (0.5–8, `StepFrequency`
  0.25), `Editor.PopupPreview` live stroke line tracking size + colour,
  `Editor.Pen.Pressure`/`Editor.Pen.InkSimulation`/
  `Editor.Pen.ShapeRecognition` toggles persisted through `SaveSetting`
  → `AppSettings`, the `Editor.Pen.Smoothing.{i}` segmented row writing
  `StrokeSmoothing`, and `ApplyToolToAllPages` on every change.
- `EraserFlyoutPortsTheWpfEraserPopupSurface` — `ShowEraserFlyout` carries
  the `Editor.Eraser.Pixel`/`Editor.Eraser.WholeStroke` mode row persisted
  to `WholeStrokeEraser` above the 4–80 `Editor.Eraser.Size` slider, with
  slider drags flashing `EraserSizePreviewEllipse` (WPF
  `ShowEraserSizePreview` ~1.2 s self-hide).
- `RecentColorsRowCoversPenHighlighterAndTextPalettes` — the
  `Editor.Color.Recent.{i}` swatch row (`BuildRecentColorsSection` +
  `RefreshRecentColorsRow`) sits above the palette in the pen,
  highlighter, and cached text-colour flyouts (the latter repopulated on
  `FlyoutBase.Opening`); the shape flyout pins its intentional absence
  (WPF keeps shape colours session-only).
- `ToolFlyoutsKeepSingleSurfaceAndTransientSweepParity` — `_toolFlyout`/
  `_toolFlyoutTool` + `CloseToolFlyouts`/`ShowToolFlyout` give WPF
  `CloseToolPopups` mutual exclusion (highlighter modes share the
  `Highlighter` owner bucket); `CloseTransientUi` sweeps the tracked
  flyout for Escape/tab-deactivate parity; `WrapToolFlyoutContent` ports
  `EnableToolPopupScrolling` (XamlRoot height for `WorkArea`).
- `CoreRecentColorsCarriesTheWpfListContract` — `RecentColors` lives in
  `OpenNotes.Core` under `Caelum.Services`, `MaxRecentColors == 8`, with
  `Record`/`TryParse` members the WinUI page calls through
  `RecordRecentColor`/`TryParseRecentColor`/`SaveSetting` wrappers.

## Open Threads / Resume Context

- Pure text contracts (`Read`/`ProjectRoot` locates the WinUI tree and
  `OpenNotes.Core`); mirrors `WinUiParitySourceTests` conventions.
- Residual documented in `docs/winui3-parity-checklist.md`: WinUI
  light-dismiss closes a tool flyout on the first ink press where WPF
  `StaysOpen` + `ArmPendingPopupDismissalGesture` kept it up to gesture
  end.
