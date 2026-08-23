# PageInsertTemplate

## Purpose

Enumerates blank-page drawing templates shared by the page picker and PdfService.

## V5 Changes

- `Dotted`, `Music` and `Cornell` extend the original Blank/Notebook/Lined/Quadrille set.
- Values are rendered as vector PDF content by PdfService and selected through PageTemplatePickerWindow.

## Constraints

- Keep enum values stable for callers and preserve existing template behavior.

## Open Threads

- No required V5 template implementation remains.
