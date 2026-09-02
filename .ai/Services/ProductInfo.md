# Services/ProductInfo.cs
> 2026-09-02 GREEN: visible version is 5.2.9 for Edge-PDF compatibility; every Caelum/WindowsNotesApp identity, data root, and URL remains unchanged.
> 2026-08-31 GREEN: visible version is 5.2.8 for the eraser stylus-crash hotfix; every Caelum/WindowsNotesApp identity, data root, and URL remains unchanged.
> 2026-08-31 GREEN: visible version is 5.2.7 for the page-rotation drawing hotfix; all Caelum/WindowsNotesApp identities, storage, and compatibility URLs remain unchanged.
> 2026-08-30 GREEN: visible version is 5.2.6 for the editor reliability, page reorder, editable shape, and detachable-tab release; legacy compatibility values remain unchanged.
> 2026-08-28 GREEN: visible version is 5.2.4 for the selection/text/ruler regression release; Caelum/WindowsNotesApp compatibility identifiers, data root and URLs are preserved.
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
- `Version`: `5.2.9`
- `Description`: localized through `LocalizationService.Get("Product.Description")`

## Important Notes / NEVER Change

- `OpenNotes` is the visible product, assembly, project and workspace name; the root `Caelum` namespace, data directory, and AppX identity remain legacy compatibility identifiers.
- The formal checkout folder is already `OpenNotes`; this is separate from the legacy data directory and namespace.
- The current repository and Pages URLs intentionally retain the existing `Windows-Notes` path; changing them requires a separately verified redirect/repository migration.
- `Description` must remain a localization key, not a hard-coded language-specific sentence.
- `GetDataDirectory()` may honor `OPENNOTES_DATA_ROOT` only when explicitly set by a test/diagnostic process; with no override it must resolve exactly to `%LOCALAPPDATA%\Caelum`.

## Open Threads / Resume Context

- **Status:** GREEN (5.2.3 editor regression-fix release)
- ProductInfoTests proved the 5.2.3 expectation RED against 5.2.2, then GREEN after this constant advanced; Caelum/WindowsNotesApp compatibility identifiers, data root and URLs remain unchanged.
- **Status:** verified (5.2.2 navigation layout patch)
- Only the visible version advances to `5.2.2`; compatibility names, data root and URLs remain unchanged.
- **Status:** ready_for_release (5.2.1 large-PDF crash hotfix)
- Only the visible version advances to `5.2.1`; compatibility names, data root
  and URLs remain unchanged.
- **Status:** ready_for_release (5.2.0 feature release)
- Only the visible product version changed to `5.2.0`; compatibility names, data root and URLs are preserved.
- **Status:** complete (5.1.2 patch release)
- Only the visible product version changed to 5.1.2; compatibility names, data root and URLs are preserved.
- **Status:** complete (5.0.1 startup-crash patch)
- Only the visible version changed; every compatibility identifier and URL remains intact.
- **Status:** ready_for_next
- `GetDataDirectory()` is implemented and used by settings, recent files, bookmarks, and version history. The opt-in override is only for isolated test/diagnostic processes; the production default remains `%LOCALAPPDATA%\Caelum`.

## Change History

- 2026-09-02: Bumped the visible version to `5.2.9` for Edge-PDF compatibility; compatibility identifiers and URLs remain unchanged.
- 2026-08-31: Bumped the visible version to `5.2.8` for the eraser stylus-crash hotfix; compatibility identifiers and URLs remain unchanged.
- 2026-08-31: Bumped the visible version to `5.2.7` for the page-rotation drawing hotfix; compatibility identifiers and URLs remain unchanged.
- 2026-08-21: Added `OPENNOTES_DATA_ROOT` as an opt-in isolated-run root while preserving the legacy production directory.
- 2026-08-24: Bumped the visible product version to `5.0.0`; legacy namespace, storage path, AppX identity, repository and website URLs remain unchanged.
- 2026-08-24: Bumped the visible product version to `5.0.1` for the home-hover startup-crash patch; compatibility identifiers and URLs remain unchanged.
- 2026-08-24: Promoted the verified startup-crash fix to visible version `5.1.0`; compatibility identifiers and URLs remain unchanged.
- 2026-08-24: Bumped the visible version to `5.1.1` for the Settings-menu crash and Light-background patch.
- 2026-08-24: Bumped the visible version to `5.1.2` for the application-wide Lucide icon release.
- 2026-08-24: Bumped the visible version to `5.2.0` for the toolbar, shapes and page-template feature release.
- 2026-08-25: Bumped the visible version to `5.2.1` for the large-PDF session-lifecycle crash hotfix.
- 2026-08-25: Bumped the visible version to `5.2.2` for the fixed sidebar and centered page navigator patch.
- 2026-08-26: Bumped the visible version to `5.2.3` for the editor regression-fix release; compatibility identifiers and URLs remain unchanged.
