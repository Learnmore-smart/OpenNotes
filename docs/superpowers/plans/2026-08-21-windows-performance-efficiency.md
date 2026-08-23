# Windows Performance and Energy Efficiency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bound PDF bitmap memory, reduce inactive and redundant rendering work, and add Balanced, Battery saver, and Best quality display modes without changing document fidelity.

**Architecture:** A pure `PdfRenderPolicy` owns mode normalization, page-retention windows, and pixel budgets. `EditorPage` applies that policy to its existing lazy render pipeline and tab lifecycle; `PdfService` supplies correctly scaled frozen bitmaps and safe async disposal; `PdfPageControl` exposes motion/lifecycle hooks without moving annotation state out of the control.

**Tech Stack:** .NET 8, WPF, C#, PdfiumViewer, NUnit 3

**Repository note:** Target source files already contain substantial user changes. Implementation commits are intentionally omitted so this pass cannot accidentally commit unrelated work; verification will leave the performance edits clearly isolated in the working-tree diff.

---

### Task 1: Add the test-first render policy and persisted mode

**Files:**
- Create: `Services/PdfRenderPolicy.cs`
- Modify: `Models/AppSettings.cs`
- Modify: `Services/AppSettingsService.cs`
- Create: `OpenNotes.Tests/PdfRenderPolicyTests.cs`

- [x] **Step 1: Write the failing policy tests**

```csharp
using Caelum.Services;

namespace OpenNotes.Tests;

public sealed class PdfRenderPolicyTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("unknown")]
    public void NormalizeMode_InvalidValuesUseBalanced(string? value)
        => Assert.That(PdfRenderPolicy.NormalizeMode(value), Is.EqualTo(PdfRenderPolicy.Balanced));

    [Test]
    public void RetainedPages_AreBoundedAtDocumentEdges()
    {
        Assert.That(PdfRenderPolicy.GetRetainedPageIndices(0, 1, 20, PdfRenderPolicy.Balanced),
            Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(PdfRenderPolicy.GetRetainedPageIndices(18, 19, 20, PdfRenderPolicy.Balanced),
            Is.EqualTo(new[] { 17, 18, 19 }));
        Assert.That(PdfRenderPolicy.GetRetainedPageIndices(8, 9, 20, PdfRenderPolicy.BatterySaver),
            Is.EqualTo(new[] { 8, 9 }));
    }

    [TestCase(PdfRenderPolicy.BatterySaver, 1.35)]
    [TestCase(PdfRenderPolicy.Balanced, 2.0)]
    [TestCase(PdfRenderPolicy.BestQuality, 3.0)]
    public void CalculateRenderScale_RespectsModeCeiling(string mode, double expected)
        => Assert.That(PdfRenderPolicy.CalculateRenderScale(mode, 816, 1056, 8), Is.EqualTo(expected).Within(0.001));

    [Test]
    public void CalculateRenderScale_ConstrainsOversizedPageToByteBudget()
    {
        var profile = PdfRenderPolicy.GetProfile(PdfRenderPolicy.Balanced);
        var scale = PdfRenderPolicy.CalculateRenderScale(PdfRenderPolicy.Balanced, 5000, 5000, 8);
        var bytes = PdfRenderPolicy.EstimateBitmapBytes(5000, 5000, scale);
        Assert.Multiple(() =>
        {
            Assert.That(scale, Is.LessThan(1));
            Assert.That(bytes, Is.LessThanOrEqualTo(profile.MaxBitmapBytes));
        });
    }

    [Test]
    public void NormalizeRequestedScale_PreservesThumbnailScale()
        => Assert.That(PdfRenderPolicy.NormalizeRequestedScale(0.22), Is.EqualTo(0.22).Within(0.0001));
}
```

- [x] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PdfRenderPolicyTests`

Expected: compilation fails because `PdfRenderPolicy` does not exist.

- [x] **Step 3: Implement the pure policy**

```csharp
namespace Caelum.Services;

public readonly record struct PdfRenderProfile(int RetainedPagePadding, bool PrefetchAdjacentPages, double MaxRenderScale, long MaxBitmapBytes);

public static class PdfRenderPolicy
{
    public const string BatterySaver = "BatterySaver";
    public const string Balanced = "Balanced";
    public const string BestQuality = "BestQuality";
    private const double PixelsPerDipAtBaseRender = 2.0;
    private const int BytesPerPixel = 4;

    public static string NormalizeMode(string? value)
    {
        if (string.Equals(value?.Trim(), BatterySaver, StringComparison.OrdinalIgnoreCase)) return BatterySaver;
        if (string.Equals(value?.Trim(), BestQuality, StringComparison.OrdinalIgnoreCase)) return BestQuality;
        return Balanced;
    }

    public static PdfRenderProfile GetProfile(string? mode) => NormalizeMode(mode) switch
    {
        BatterySaver => new(0, false, 1.35, 32L * 1024 * 1024),
        BestQuality => new(1, true, 3.0, 128L * 1024 * 1024),
        _ => new(1, true, 2.0, 64L * 1024 * 1024)
    };

    public static IReadOnlyList<int> GetRetainedPageIndices(int firstVisible, int lastVisible, int pageCount, string? mode)
    {
        if (pageCount <= 0 || firstVisible < 0 || lastVisible < firstVisible) return Array.Empty<int>();
        var padding = GetProfile(mode).RetainedPagePadding;
        var first = Math.Max(0, firstVisible - padding);
        var last = Math.Min(pageCount - 1, lastVisible + padding);
        return Enumerable.Range(first, last - first + 1).ToArray();
    }

    public static double NormalizeRequestedScale(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0.1, 3.0) : 1.0;

    public static long EstimateBitmapBytes(double widthDips, double heightDips, double scale)
    {
        if (!double.IsFinite(widthDips) || !double.IsFinite(heightDips) || widthDips <= 0 || heightDips <= 0) return 0;
        var normalizedScale = NormalizeRequestedScale(scale);
        var bytes = widthDips * PixelsPerDipAtBaseRender * normalizedScale
            * heightDips * PixelsPerDipAtBaseRender * normalizedScale * BytesPerPixel;
        return bytes >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(bytes);
    }

    public static double CalculateRenderScale(string? mode, double widthDips, double heightDips, double requestedScale)
    {
        var profile = GetProfile(mode);
        var requested = Math.Min(Math.Max(NormalizeRequestedScale(requestedScale), 0.25), profile.MaxRenderScale);
        var baseBytes = EstimateBitmapBytes(widthDips, heightDips, 1.0);
        if (baseBytes <= 0) return Math.Min(1.0, requested);
        var budgetScale = Math.Sqrt((double)profile.MaxBitmapBytes / baseBytes);
        return Math.Clamp(Math.Min(requested, budgetScale), 0.25, profile.MaxRenderScale);
    }
}
```

Add `public string PerformanceMode { get; set; } = "Balanced";` to `AppSettings`. Normalize it with `PdfRenderPolicy.NormalizeMode` in `AppSettingsService.Sanitize`, and copy it in both service snapshot paths.

- [x] **Step 4: Run the focused tests and verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PdfRenderPolicyTests`

Expected: all `PdfRenderPolicyTests` pass.

### Task 2: Expose localized performance modes in Settings

**Files:**
- Modify: `SettingsWindow.xaml`
- Modify: `SettingsWindow.xaml.cs`
- Modify: `Services/LocalizationService.cs`
- Modify: `OpenNotes.Tests/LocalizationServiceTests.cs`

- [x] **Step 1: Add failing localization and settings source-contract assertions**

Add required keys `Settings.Performance`, `Settings.PerformanceBatterySaver`, `Settings.PerformanceBalanced`, and `Settings.PerformanceBestQuality` to the catalog completeness test. Assert that `SettingsWindow.xaml` contains `PerformanceModeComboBox` and `SettingsWindow.xaml.cs` copies `PerformanceMode`.

- [x] **Step 2: Run the focused test and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter LocalizationServiceTests`

Expected: missing performance keys/control assertions fail.

- [x] **Step 3: Implement the settings row and three-language strings**

Add an eighth utility row containing `PerformanceModeLabelTextBlock` and `PerformanceModeComboBox`. Register its popup with `PopupZOrderHelper`, preserve its selected index while relocalizing, and map indices as follows:

```csharp
private static int GetPerformanceModeIndex(string value) => PdfRenderPolicy.NormalizeMode(value) switch
{
    PdfRenderPolicy.BatterySaver => 0,
    PdfRenderPolicy.BestQuality => 2,
    _ => 1
};

private static string GetPerformanceModeValue(int index) => index switch
{
    0 => PdfRenderPolicy.BatterySaver,
    2 => PdfRenderPolicy.BestQuality,
    _ => PdfRenderPolicy.Balanced
};
```

The displayed choices are localized Battery saver, Balanced (recommended), and Best quality. `GetSelectedSettings` and `CloneSettings` copy the persisted value. The ComboBox participates in the existing immediate preview handler.

- [x] **Step 4: Run localization tests and audit**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter LocalizationServiceTests`

Run: `powershell -ExecutionPolicy Bypass -File tools/verify-i18n.ps1`

Expected: tests pass; all catalogs have equal key and placeholder sets with no hard-coded visible settings strings.

### Task 3: Correct bitmap scaling and add safe Pdfium disposal

**Files:**
- Modify: `Services/PdfService.cs`
- Modify: `OpenNotes.Tests/PdfRenderPolicyTests.cs`

- [x] **Step 1: Add failing render-contract tests**

Assert that `NormalizeRequestedScale(0.22)` produces a render DPI of 42 and that invalid/non-finite scales produce 192 DPI. Add a public `CalculateRenderDpi` policy helper if needed so no native PDF is required.

- [x] **Step 2: Verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PdfRenderPolicyTests`

Expected: `CalculateRenderDpi` is missing.

- [x] **Step 3: Implement correct scale and disposal**

Use `PdfRenderPolicy.CalculateRenderDpi(dpiScale)` in `RenderPageBitmapSourceAsync`, allowing 0.22× thumbnails. Keep `BitmapSource.Freeze()` and the direct GDI `LockBits` path. Implement idempotent `IAsyncDisposable.DisposeAsync` by acquiring `_documentLock`, calling `DisposeCurrentDocument`, releasing the semaphore, then disposing it only after no render can re-enter.

- [x] **Step 4: Verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PdfRenderPolicyTests`

Expected: render policy tests pass.

### Task 4: Make page bitmap swaps and render-loop activity lifecycle aware

**Files:**
- Modify: `Controls/PdfPageControl.xaml.cs`
- Create: `OpenNotes.Tests/PerformanceLifecycleSourceTests.cs`

- [x] **Step 1: Add failing lifecycle source contracts**

Read `Controls/PdfPageControl.xaml.cs` and assert it exposes `SetHostActive(bool)`, uses direct assignment when `PdfImage.Source == null`, stops selection animation on suspension, and restores it only when a selection remains.

- [x] **Step 2: Verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PerformanceLifecycleSourceTests`

Expected: lifecycle contract assertions fail.

- [x] **Step 3: Implement the page lifecycle hooks**

`SetHostActive(false)` stops selection rendering, reveal timers, and transient laser visuals. `SetHostActive(true)` restarts selection dash animation only when the selected collections are non-empty. Add `SetBitmapScalingMode(BitmapScalingMode)` for the two PDF images. In `SwapPageSource`, assign the first non-null source directly when both image layers are empty; retain the guarded multi-frame swap for replacements.

- [x] **Step 4: Verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PerformanceLifecycleSourceTests`

Expected: page lifecycle contracts pass.

### Task 5: Bound EditorPage render and thumbnail working sets

**Files:**
- Modify: `Pages/EditorPage.xaml.cs`
- Modify: `OpenNotes.Tests/PerformanceLifecycleSourceTests.cs`

- [x] **Step 1: Add failing editor working-set contracts**

Assert source contains `TrimPageBitmapWorkingSet`, `SetHostActive`, `ReleaseResourcesAsync`, a bounded thumbnail cache constant, mode-controlled adjacent prefetch, and calls to `PdfRenderPolicy.CalculateRenderScale`.

- [x] **Step 2: Verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PerformanceLifecycleSourceTests`

Expected: editor lifecycle and working-set assertions fail.

- [x] **Step 3: Implement bounded rendering**

Keep the existing all-page tool propagation unchanged. Use the visible range plus `PdfRenderPolicy.GetRetainedPageIndices` to clear `PageSource` and render-set membership outside the working set. Use the per-page policy scale in zoom rendering. Skip adjacent prefetch in Battery saver.

Replace repeated scroll delay pipelines with one restartable dispatcher debounce and a cancellation generation for active native work. Set visible images to `LowQuality` during motion and restore `HighQuality` after the settled pass.

Keep at most 24 true low-resolution thumbnails in an access-ordered cache. When an item leaves the realized sidebar and is not cached, clear its image source. Cancel thumbnail work and clear all thumbnail sources while the tab is inactive.

`SetHostActive(false)` cancels render work, stops smooth-scroll render hooks, suspends page controls, and releases all bitmap sources. `SetHostActive(true)` resumes page controls and renders the visible range. `ReleaseResourcesAsync` is idempotent and awaits `PdfService.DisposeAsync` after cancellation.

- [x] **Step 4: Verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PerformanceLifecycleSourceTests`

Expected: editor working-set contracts pass.

### Task 6: Connect active-tab and close lifecycle

**Files:**
- Modify: `MainWindow.xaml.cs`
- Modify: `OpenNotes.Tests/PerformanceLifecycleSourceTests.cs`

- [x] **Step 1: Add failing shell lifecycle assertions**

Assert `ActivateTab` calls `SetHostActive(false)` for the previous editor and `SetHostActive(true)` for the selected editor. Assert `CloseTab` and window closing await `ReleaseResourcesAsync` after autosave.

- [x] **Step 2: Verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PerformanceLifecycleSourceTests`

Expected: MainWindow lifecycle assertions fail.

- [x] **Step 3: Wire the lifecycle**

Deactivate the old editor before collapsing frames, activate the selected editor after its frame becomes visible, and await idempotent editor resource release on tab close and application close. Do not change the `_tabs`/Frame architecture or autosave ordering.

- [x] **Step 4: Verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter PerformanceLifecycleSourceTests`

Expected: shell lifecycle contracts pass.

### Task 7: Full verification and documentation sync

**Files:**
- Modify: `.ai/PROJECT_CONTEXT.md`
- Modify matching `.ai` mirrors for every changed source and test file

- [x] **Step 1: Run the complete automated suite**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore`

Expected: all tests pass with zero failures.

- [x] **Step 2: Build the solution**

Run: `dotnet build OpenNotes.sln --no-restore`

Expected: exit code 0; only the existing PdfiumViewer compatibility warnings are acceptable.

- [x] **Step 3: Run localization and desktop smoke checks**

Run: `powershell -ExecutionPolicy Bypass -File tools/verify-i18n.ps1`

Run: `powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesEditorSmoke.ps1`

Expected: localization audit and `EDITOR_SMOKE_RESULT=PASS`.

- [x] **Step 4: Inspect hygiene and diff**

Run: `git diff --check`

Run: `git diff --stat`

Expected: no whitespace errors; only performance/settings implementation, tests, spec/plan, and `.ai` mirrors are changed by this pass.

**Completed verification (2026-08-21):** Release build succeeded with 0 errors; 95/95 tests passed; i18n verified 272 catalog entries and 385 calls with 0 hard-coded visible strings; both the isolated settings UI automation and one-page editor smoke passed; `git diff --check` reported no whitespace errors (only the repository's existing LF/CRLF notices).
