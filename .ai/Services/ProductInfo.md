# Services/ProductInfo.cs
> Last updated: 2026-08-24（5.0.0 release metadata）| Protection: STANDARD

## Purpose
Single source of truth for the visible OpenNotes brand and the compatibility identifiers that must remain Caelum/WindowsNotesApp.

## Public API

- `DisplayName`: `OpenNotes`
- `LegacyName`: `Caelum`
- `LegacyDataDirectoryName`: `Caelum` (`%LOCALAPPDATA%\Caelum`)
- `LegacyAppxIdentity`: `WindowsNotesApp`
- `RepositoryUrl`: `https://github.com/Learnmore-smart/Windows-Notes`
- `WebsiteUrl`: `https://learnmore-smart.github.io/Windows-Notes/`
- `Version`: `5.0.1`
- `Description`: localized through `LocalizationService.Get("Product.Description")`

## Important Notes / NEVER Change

- `OpenNotes` is the visible product, assembly, project and workspace name; the root `Caelum` namespace, data directory, and AppX identity remain legacy compatibility identifiers.
- The formal checkout folder is already `OpenNotes`; this is separate from the legacy data directory and namespace.
- The current repository and Pages URLs intentionally retain the existing `Windows-Notes` path; changing them requires a separately verified redirect/repository migration.
- `Description` must remain a localization key, not a hard-coded language-specific sentence.
- `GetDataDirectory()` may honor `OPENNOTES_DATA_ROOT` only when explicitly set by a test/diagnostic process; with no override it must resolve exactly to `%LOCALAPPDATA%\Caelum`.

## Open Threads / Resume Context

- **Status:** complete (5.0.1 startup-crash patch)
- Only the visible version changed; every compatibility identifier and URL remains intact.
- **Status:** ready_for_next
- `GetDataDirectory()` is implemented and used by settings, recent files, bookmarks, and version history. The opt-in override is only for isolated test/diagnostic processes; the production default remains `%LOCALAPPDATA%\Caelum`.

## Change History

- 2026-08-21: Added `OPENNOTES_DATA_ROOT` as an opt-in isolated-run root while preserving the legacy production directory.
- 2026-08-24: Bumped the visible product version to `5.0.0`; legacy namespace, storage path, AppX identity, repository and website URLs remain unchanged.
- 2026-08-24: Bumped the visible product version to `5.0.1` for the home-hover startup-crash patch; compatibility identifiers and URLs remain unchanged.
