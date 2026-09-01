# Check for Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manual, localized More-menu command that checks the latest stable GitHub Release, compares it with the installed assembly version, and offers a safe release-page link.

**Architecture:** A focused `UpdateCheckService` owns the GitHub request, strict release parsing, normalized four-part version comparison, timeout, and trusted-URL validation. `MainWindow` owns command busy/cancellation state, localized dialogs, and shell-opening the validated release page.

**Tech Stack:** .NET 8, WPF, `HttpClient`, `System.Text.Json`, NUnit 3, `LocalizationService`, and `DialogService`.

---

## File Map

- Create `Services/UpdateCheckService.cs` and `OpenNotes.Tests/UpdateCheckServiceTests.cs`.
- Modify `MainWindow.xaml`, `MainWindow.xaml.cs`, and `MainWindow.Utilities.cs`.
- Modify `Services/LocalizationService.cs` and `OpenNotes.Tests/LocalizationServiceTests.cs`.
- Create `OpenNotes.Tests/MainWindowUpdateCheckSourceTests.cs`.
- Create/update matching `.ai` mirrors before code and synchronize them after verification.

### Task 1: Update-check service contract

**Files:**
- Create: `.ai/Services/UpdateCheckService.md`
- Create: `.ai/OpenNotes.Tests/UpdateCheckServiceTests.md`
- Create: `OpenNotes.Tests/UpdateCheckServiceTests.cs`
- Create: `Services/UpdateCheckService.cs`

- [ ] **Step 1: Create and read the File Guardian mirrors**

Record: no UI/process launching; injected `HttpClient`; only the configured repository's HTTPS release URLs; four-part version normalization; categorized failures.

- [ ] **Step 2: Write failing service tests**

Use a deterministic `HttpMessageHandler` and assert:

```csharp
var service = new UpdateCheckService(client);
UpdateCheckResult result = await service.CheckAsync(new Version(5, 2, 7, 0));
Assert.That(result.LatestVersion, Is.EqualTo(new Version(5, 3, 0, 0)));
Assert.That(result.IsUpdateAvailable, Is.True);
Assert.That(result.ReleaseUri.AbsoluteUri,
    Is.EqualTo("https://github.com/Learnmore-smart/Windows-Notes/releases/tag/v5.3.0"));
```

Cover newer/equal/local-newer versions; leading `v`; two-to-four parts; malformed tags/JSON; unsafe URLs; HTTP errors; transport; timeout; cancellation; and required headers.

- [ ] **Step 3: Run the focused test and verify RED**

```powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter FullyQualifiedName~UpdateCheckServiceTests --verbosity quiet
```

Expected: compile failure because the service/result/exception types do not exist.

- [ ] **Step 4: Implement the minimal service**

```csharp
public enum UpdateCheckFailureKind { Network, Timeout, HttpStatus, InvalidResponse }

public sealed record UpdateCheckResult(
    Version InstalledVersion, Version LatestVersion, Uri ReleaseUri)
{
    public bool IsUpdateAvailable => LatestVersion > InstalledVersion;
}

public sealed class UpdateCheckException : Exception
{
    public UpdateCheckFailureKind Kind { get; }
}

public sealed class UpdateCheckService
{
    public static readonly Uri LatestReleaseApiUri =
        new("https://api.github.com/repos/Learnmore-smart/Windows-Notes/releases/latest");
    public UpdateCheckService(HttpClient? httpClient = null);
    public Task<UpdateCheckResult> CheckAsync(
        Version installedVersion, CancellationToken cancellationToken = default);
    internal static Version ParseReleaseVersion(string tag);
    internal static bool IsTrustedReleaseUri(Uri uri);
}
```

Use a linked 10-second cancellation source. Send GitHub `Accept`, API-version, and `OpenNotes/{ProductInfo.Version}` User-Agent headers. Deserialize `tag_name` and `html_url`. Pad versions to four parts. Require HTTPS, `github.com`, and the configured repository's `/releases/` path. Preserve caller cancellation; categorize timeout, HTTP, transport, and invalid responses.

- [ ] **Step 5: Run the focused test and verify GREEN**

Re-run Step 3. Expected: all `UpdateCheckServiceTests` pass.

- [ ] **Step 6: Commit the service slice**

Run `git add -- Services/UpdateCheckService.cs OpenNotes.Tests/UpdateCheckServiceTests.cs .ai/Services/UpdateCheckService.md .ai/OpenNotes.Tests/UpdateCheckServiceTests.md`, then `git commit -m 'feat: add GitHub update check service'`.

### Task 2: Localized MainWindow command

**Files:**
- Read/update: `.ai/MainWindow.md`, `.ai/MainWindow.xaml.md`, `.ai/MainWindow.Utilities.md`
- Read/update: `.ai/Services/LocalizationService.md`, `.ai/OpenNotes.Tests/LocalizationServiceTests.md`
- Create: `.ai/OpenNotes.Tests/MainWindowUpdateCheckSourceTests.md`
- Modify: `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.Utilities.cs`
- Modify: `Services/LocalizationService.cs`, `OpenNotes.Tests/LocalizationServiceTests.cs`
- Create: `OpenNotes.Tests/MainWindowUpdateCheckSourceTests.cs`

- [ ] **Step 1: Record the scoped UI plan in all mirrors**

Preserve `MoreButton` AutomationId, popup z-order lifecycle, owner-modal dialogs, theme resources, and existing Settings/About behavior. Order commands Settings → Check for updates → About; never start a check automatically.

- [ ] **Step 2: Write failing localization and source-contract tests**

Assert exact EN/ZH/FR entries for `Main.CheckForUpdates`, `Main.CheckingForUpdates`, `Main.UpdateAvailableTitle`, `Main.UpdateAvailableMessage` ({0}, {1}), `Main.UpToDateTitle`, `Main.UpToDateMessage` ({0}), `Main.UpdateCheckFailedTitle`, `Main.UpdateCheckFailedMessage`, and `Main.ViewRelease`.

Create source assertions that verify menu ordering/click wiring, localization refresh, a `finally` restoration of `CheckForUpdatesMenuItem.IsEnabled`, close-time cancellation, and a second `UpdateCheckService.IsTrustedReleaseUri` check before shell launch.

- [ ] **Step 3: Run focused tests and verify RED**

Run `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter FullyQualifiedName~LocalizationCoverageTests|FullyQualifiedName~MainWindowUpdateCheckSourceTests --verbosity quiet`.

Expected: failures for missing catalog entries, menu item, click handler, and lifecycle source.

- [ ] **Step 4: Add the localized menu and handler**

Insert:

```xml
<MenuItem x:Name="SettingsMenuItem" Click="Settings_Click"/>
<MenuItem x:Name="CheckForUpdatesMenuItem" Click="CheckForUpdates_Click"/>
<MenuItem x:Name="AboutMenuItem" Click="About_Click"/>
```

Add the menu header to `ApplyLocalization()`. Add one service instance, a busy flag, and a cancellation source to MainWindow. The click handler disables/renames the item, reads `Assembly.GetEntryAssembly()?.GetName().Version`, awaits `CheckAsync`, shows the up-to-date or available dialog, and opens only a revalidated trusted release URI. Catch `UpdateCheckException` and browser `Win32Exception` with the localized retry message. In `finally`, dispose the cancellation source, clear busy state, re-enable the item, and restore its localized header. Cancel the request in the existing `Closed` callback without showing a shutdown error.

- [ ] **Step 5: Run focused tests and verify GREEN**

Re-run Step 3. Expected: all selected tests pass.

- [ ] **Step 6: Commit the UI/localization slice**

Stage only Task 2 files and mirrors; exclude concurrent eraser files. Commit with `feat: add localized update check command`.

### Task 3: Verification and handoff

**Files:**
- Synchronize all mirrors touched by Tasks 1–2.
- Update `.ai/PROJECT_CONTEXT.md` only with an isolated patch that preserves concurrent lines.

- [ ] **Step 1: Run focused update-check tests**

Run `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter FullyQualifiedName~UpdateCheckServiceTests|FullyQualifiedName~MainWindowUpdateCheckSourceTests|FullyQualifiedName~LocalizationCoverageTests --verbosity quiet`.

- [ ] **Step 2: Run full verification**

Run:

```powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --verbosity quiet
dotnet build OpenNotes.sln -c Release --no-restore --verbosity quiet
powershell -ExecutionPolicy Bypass -File tools/verify-i18n.ps1
git diff --check
```

Expected: full suite passes; Release build has zero errors; i18n has equal language keys and zero hard-coded visible strings; diff check exits zero apart from known line-ending warnings.

- [ ] **Step 3: Synchronize mirrors and review the scoped diff**

Record exact verification evidence, clear this feature's Open Threads, and verify with `git status --short`, path-scoped diffs, and `git diff --check`. Leave unrelated dirty files untouched.
