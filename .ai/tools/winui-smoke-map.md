# tools/winui-smoke-map.md
> Last updated: 2026-09-25 (V6 Task 10) | Protection: STANDARD

## Purpose
Documentation-only file: maps each WPF `tools/Test-OpenNotes*.ps1` driver to
its WinUI `tools/winui-*.ps1` equivalent (or "no equivalent yet") and records
the sandboxed-environment input-injection limitation for the pointer smoke.
See `docs/winui3-parity-checklist.md` for the feature-level audit.

## Important Notes / NEVER Change
- WinUI smokes launch `OpenNotes.WinUI.exe` under an isolated
  `OPENNOTES_DATA_ROOT`; agents must not run them headlessly (app launch).
- `winui-uia-pointer-smoke.ps1` needs a real interactive desktop — SendInput
  does not reach the window from sandboxed sessions; DEBUG invoke seams
  (`Editor.Debug*`) cover those paths in `winui-editor-smoke.ps1` instead.
