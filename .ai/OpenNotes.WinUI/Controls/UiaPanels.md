# OpenNotes.WinUI/Controls/UiaPanels.cs
> Last updated: 2026-09-23 (V6 Task 6 — UIA container visibility) | Protection: STANDARD

## Purpose
Thin subclasses that surface `FrameworkElementAutomationPeer` on layout
containers. In WinUI 3, `Grid`/`StackPanel`/`Border` create **no**
AutomationPeer, so any `AutomationId` on them is invisible to UIA clients
(unlike WPF where every element surfaces). The WPF editor shell exposes
container AutomationIds the smoke harness searches for.

## What It Does
- `UiaStackPanel : StackPanel`, `UiaGrid : Grid` — same layout, plus a peer.
- `UiaContentControl : ContentControl` — `Border` is sealed in WinUI 3, so
  border-styled containers that need a UIA presence wrap their `Border` child
  in this host (`PagesContainer`, `DocumentSidebar`, `PdfSearchPanel` use it).

## Important Notes / NEVER Change
- Use only where a container AutomationId must be discoverable; don't blanket-
  convert every panel (peers add UIA-tree noise).
- `Editor.PageJumpGroup`, `DocumentSidebar`, `PdfSearchPanel`, `PagesContainer`
  are load-bearing ids — keep them on the Uia* hosts.

## Open Threads / Resume Context
- **Status:** GREEN — all container ids found by `winui-editor-smoke.ps1`.
