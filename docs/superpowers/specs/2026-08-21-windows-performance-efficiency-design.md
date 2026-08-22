# Windows Performance and Energy Efficiency Design

## Goal

Keep OpenNotes smooth during drawing, scrolling, and zooming while bounding PDF bitmap memory, preventing inactive tabs from consuming rendering resources, and giving users a simple performance preference that never changes saved-document fidelity.

## User experience

The default mode is **Balanced**. It keeps adjacent pages ready, uses high-quality rendering after motion settles, and caps oversized zoom bitmaps before they can cause memory pressure or long native render stalls.

Settings also exposes:

- **Battery saver**: renders only the immediate working area, uses a lower zoom-render ceiling, and avoids adjacent-page prefetch.
- **Best quality**: allows a larger zoom-render budget and adjacent-page prefetch while retaining the same bounded off-screen cache.

Changing this setting applies immediately to open editors and is reversible through the existing Settings preview/cancel flow. The setting affects only display caches and scheduling. PDF annotations, export, autosave, undo, coordinates, and persisted image quality remain unchanged.

## Architecture

### Pure performance policy

A new `PdfRenderPolicy` maps the persisted mode to immutable limits:

| Mode | Retained-page padding | Adjacent prefetch | Maximum zoom render scale | Maximum bytes per page bitmap |
|---|---:|---:|---:|---:|
| Battery saver | 0 | No | 1.35× | 32 MiB |
| Balanced | 1 | Yes | 2.0× | 64 MiB |
| Best quality | 1 | Yes | 3.0× | 128 MiB |

The policy computes:

- a bounded render scale from page dimensions, requested zoom, mode scale ceiling, and the per-bitmap byte ceiling;
- the page indices retained around the visible range;
- whether adjacent prefetch is permitted;
- thumbnail scale normalization, allowing the existing 0.22× request to render below the current 1.0× floor.

Keeping these calculations independent of WPF makes their boundary behavior deterministic and unit-testable.

### Main PDF bitmap working set

`EditorPage` remains the owner of page controls and annotation state. After initial load, scrolling, zoom rendering, and tab activation, it computes the retained page window. Pages outside that window have `PageSource` cleared and are removed from the initial/high-resolution render sets. Re-entering an evicted page renders it through the existing lazy path.

This bounds full-page bitmap memory by the visible pages plus a small mode-controlled margin, regardless of how many pages the user has visited. Interactive `PdfPageControl` instances and annotation layers remain in place, avoiding a risky annotation-model virtualization rewrite.

High-resolution rendering applies only to visible pages. The render scale is calculated per page so unusually large page dimensions cannot bypass the byte budget. Adjacent pages use the normal base render.

### Thumbnail working set

`PdfService.RenderPageBitmapSourceAsync` will honor sub-1.0 scale requests with a safe lower bound, so the current 0.22× thumbnail request produces a true thumbnail instead of a full 192-DPI page.

The sidebar keeps only a small least-recently-used thumbnail cache. Unloaded/recycled thumbnail visuals release their source unless that source remains in the bounded cache. This prevents a long sidebar session from rebuilding an unbounded bitmap collection.

### Scheduling and motion quality

Scroll-triggered rendering is coalesced so repeated `ScrollChanged` events restart one short debounce instead of allocating a fresh delay pipeline for every event. A new scroll generation cancels obsolete work and stale results never replace a newer page source.

During active scrolling or zooming, visible page images use WPF's faster bitmap scaling mode. Once input settles and the bounded render pass completes, visible images return to high-quality scaling. Initial page assignment bypasses the multi-frame swap when no old bitmap exists; the existing guarded two-layer swap remains for visible bitmap replacement.

### Tab lifecycle and resource release

`MainWindow` tells each `EditorPage` whether its tab is active.

When an editor becomes inactive it:

- cancels pending scroll/zoom/thumbnail rendering;
- stops smooth-scroll and selection render-loop activity;
- restores any temporarily revealed Hidden Ink and clears transient laser visuals;
- releases all PDF and thumbnail bitmap sources while retaining annotations and editor state.

When reactivated it restarts the autosave cadence if needed, reapplies the current policy, and renders the visible window. Autosave safety is preserved; deactivation does not discard dirty state.

When a tab closes, `MainWindow` awaits editor shutdown after its existing autosave. Shutdown cancels work, detaches global render hooks, releases bitmaps, and asynchronously disposes the Pdfium document behind its existing semaphore so disposal cannot race a native render.

## Settings and localization

`AppSettings.PerformanceMode` stores `Balanced`, `BatterySaver`, or `BestQuality`, defaulting to `Balanced` for old settings files. `AppSettingsService` normalizes invalid values and preserves the field in every clone. `SettingsWindow` adds one localized ComboBox and includes it in preview, save, cancel, and popup z-order handling. English, Simplified Chinese, and French catalog entries cover the label and three choices.

## Error handling

- Cancellation remains silent for obsolete render work.
- Individual render failures leave the page eligible for a later retry.
- Scale calculations reject non-finite or invalid page dimensions and fall back to the mode's safe base behavior.
- Resource shutdown is idempotent so tab close, navigation unload, and window close cannot double-dispose native state.
- No forced garbage collection is introduced; releasing WPF bitmap references allows normal workstation GC and graphics-resource reclamation to work without UI pauses.

## Testing and verification

Automated tests cover:

- mode normalization and settings clone/persistence behavior;
- retained-page windows at document boundaries;
- render-scale byte caps for ordinary and oversized pages;
- true sub-1.0 thumbnail scaling;
- source-level lifecycle contracts for tab activation and safe shutdown where direct WPF automation is impractical.

Verification includes the full unit suite, solution build, localization audit, existing isolated WPF editor smoke, and a synthetic long-document check that confirms the number and estimated bytes of retained page bitmaps remain bounded after traversing the document.

## Non-goals and invariants

- Do not replace the single-window Frame/tab architecture.
- Do not virtualize annotation models or remove off-screen `PdfPageControl` instances in this pass.
- Do not change strip-and-rebuild PDF loading, annotation ownership, DIP/PDF coordinate conversion, atomic saves, undo commands, or autosave semantics.
- Do not use forced GC, process priority changes, timer-resolution changes, or permanent low-quality rendering.
