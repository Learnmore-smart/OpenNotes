# SettingsWindow.xaml

> Last updated: 2026-08-24 | Protection: STANDARD

## Purpose

Defines the localized, theme-aware Settings dialog layout and control visuals.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** replace the dense utility grid with calmer spacing, remove the visible default-pen color/size rows, expand workspace backdrop choices, and replace square checkbox visuals with rounded accessible switches.
- **Constraints:** preserve control automation identifiers used by the Settings UIA smoke, reversible preview/Cancel semantics, keyboard focus, three-language localization, and the bounded resizable/scrollable dialog.

## Important Notes / NEVER Change

- Visible strings remain sourced from `LocalizationService`.
- Theme/backdrop preview must remain reversible on Cancel.
- Do not tint PDF page pixels when styling the workspace selector.

## Completion

- Implemented the pill switch template, removed the two raw default-pen rows, and added swatch-backed six-choice workspace selection without changing persisted compatibility fields.
