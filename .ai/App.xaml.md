# App.xaml

> Last updated: 2026-08-24 | Protection: CRITICAL

## Purpose

Defines global WPF theme resources and shared control templates.

## Important Notes / NEVER Change

- Preserve dynamic theme resources, keyboard focus visuals and control-template bindings.
- Shared decorative icons use Lucide vector geometry; accessible names remain on owning controls.

## Current Change

- V5.1.2 replaces the ComboBox font chevron with an inline Lucide path so standalone ResourceDictionary parsing remains supported.
