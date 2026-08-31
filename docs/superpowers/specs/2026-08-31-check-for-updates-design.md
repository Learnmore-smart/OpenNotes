# Check for Updates Design

**Goal:** Add a user-triggered, localized update check to OpenNotes that compares the installed version with the latest stable GitHub Release and lets the user open that release page.

## User Experience

The MainWindow More menu gains `Check for updates` between Settings and About. Selecting it temporarily disables that menu item and performs one update request. Repeated clicks cannot create concurrent requests.

If a newer stable version exists, OpenNotes displays the installed and latest versions in the existing owner-modal dialog. The primary action is `View release`; selecting it opens the exact GitHub release URL in the user's default browser. Closing or cancelling the dialog leaves the application unchanged.

If the installed version is current or newer, the dialog states that OpenNotes is up to date and includes the installed version. Network failure, timeout, rate limiting, malformed responses, and an invalid release tag produce a localized error dialog with a retry-oriented message. The menu item is re-enabled after every outcome.

## Architecture

Add a focused `UpdateCheckService` under `Services`. It receives an `HttpClient` so unit tests can provide a deterministic message handler. The service requests:

`https://api.github.com/repos/Learnmore-smart/Windows-Notes/releases/latest`

The request supplies GitHub-compatible `Accept` and `User-Agent` headers and uses a 10-second timeout. The response model consumes only `tag_name` and `html_url`. GitHub's `releases/latest` endpoint represents the latest published, non-draft, non-prerelease release, so no broader release enumeration is needed.

The service normalizes a leading `v`, accepts two-to-four numeric version components, pads missing components with zero, and compares four-component `System.Version` values. Thus `5.2.7` and `5.2.7.0` are equal. It returns a small result containing the installed version, latest version, release URL, and whether an update is available. It does not show UI or launch processes. Non-success HTTP responses, transport failures, timeouts, and invalid payloads throw a service-specific update-check exception with a failure category; the caller maps every category to the same safe localized retry message rather than interpreting failure as “up to date.”

MainWindow owns the click handler, busy state, localized dialogs, and browser launch. The current version comes from `Assembly.GetEntryAssembly().GetName().Version`, keeping the check aligned with the release build's assembly metadata without parsing informational-version commit suffixes. Browser launch uses shell execution only after validating that the service returned an absolute HTTPS GitHub URL whose host is exactly `github.com`.

## Localization and UI Integration

Add English, Simplified Chinese, and French catalog entries for the menu label, checking state/result titles, current/latest version messages, failure message, and `View release` action. `ApplyLocalization()` updates the new menu item alongside Settings and About. Existing dialog styles, theme resources, owner modality, keyboard behavior, and the stable `MoreButton` AutomationId remain unchanged.

No progress window is introduced. The disabled menu command is the only transient busy affordance because the request is short and explicitly initiated by the user.

## Error and Safety Boundaries

- The check is manual only; startup and background networking are out of scope.
- No executable, archive, or installer is downloaded or launched.
- Only an absolute HTTPS URL on `github.com` is opened.
- Drafts and prereleases are not offered.
- Cancellation/window shutdown and network errors do not mutate settings or document state.
- A response that cannot be confidently parsed is reported as a failure, never as a successful “up to date” result.

## Verification

Follow RED/GREEN development with deterministic tests covering:

- newer, equal, and locally newer semantic versions;
- leading `v` and shortened numeric version tags;
- malformed tags, malformed JSON, missing/unsafe URLs, timeout, and non-success HTTP responses;
- request URI and required headers;
- More-menu placement, click wiring, localization refresh, and three-language catalog completeness;
- busy-state restoration on success, failure, and browser-launch failure.

Run the focused update/localization tests first, then the full test suite, Release build, i18n verifier, and `git diff --check`. No live GitHub request is required for automated tests; a manual desktop smoke may verify the real endpoint and browser action when the environment permits.

## Out of Scope

- Automatic update checks, scheduled checks, notifications, downloads, installation, restart orchestration, release channels, prerelease opt-in, or skipped-version preferences.
