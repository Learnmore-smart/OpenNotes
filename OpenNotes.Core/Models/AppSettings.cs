namespace Caelum.Models
{
    using System.Collections.Generic;

    public sealed class AppSettings
    {
        public AppLanguage Language { get; set; } = AppLanguage.English;
        public bool EnablePressure { get; set; } = true;
        public bool WholeStrokeEraser { get; set; } = false;
        public bool InkSimulation { get; set; } = false;
        public bool ShapeRecognition { get; set; } = false;

        // Task 15: pen-only drawing mode (palm rejection). When true, only
        // stylus/pen input creates ink; mouse and finger touch are blocked
        // from ink creation (touch keeps panning).
        public bool PenOnlyMode { get; set; } = false;

        // Task 14: recently used colors per palette, newest first.
        // Hex strings ("#RRGGBB"), capped at 8 entries by the EditorPage
        // recording helper. Shape colors stay session-only (no list).
        public List<string> RecentPenColors { get; set; } = new List<string>();
        public List<string> RecentHighlighterColors { get; set; } = new List<string>();
        public List<string> RecentTextColors { get; set; } = new List<string>();

        // Task 23: pen preset slots (3 toolbar slots). Each slot captures a
        // tool ("Pen" | "Highlighter"), its color and size. Left-click on a
        // slot applies the preset; right-click captures the current tool
        // state into the slot. EditorPage may render in-memory fallback
        // visuals, but an empty list remains empty and is not written back;
        // Sanitize only deep-copies.
        public List<PenPreset> PenPresets { get; set; } = new List<PenPreset>();

        // Task 24: stroke smoothing level applied to freshly collected
        // freehand strokes. 0=Off (raw trajectory), 1=Low, 2=Medium
        // (default), 3=High. Maps to a moving-average window in
        // PdfPageControl.ApplySmoothing; Off also disables FitToCurve
        // rendering for the true raw polyline.
        public int StrokeSmoothing { get; set; } = 2;

        // Task 38/39: application-level preferences.
        public int AutoSaveIntervalSeconds { get; set; } = 60;
        public string DefaultPenColorHex { get; set; } = "#000000";
        public double DefaultPenSize { get; set; } = 1.5;
        public string Theme { get; set; } = "Light";
        // Wave5: editor desk/surround decoration only. PDF page pixels remain
        // an opaque, independent paper layer in PdfPageControl.
        public string WorkspaceBackdrop { get; set; } = "Neutral";

        // Display-only PDF rendering policy. Balanced is the safe default for
        // existing settings files; this never changes saved PDF fidelity.
        public string PerformanceMode { get; set; } = "Balanced";
    }

    /// <summary>
    /// Task 23: one pen preset slot — tool kind ("Pen" | "Highlighter"),
    /// color hex ("#RRGGBB") and stroke size. Persisted inside
    /// <see cref="AppSettings.PenPresets"/>.
    /// </summary>
    public sealed class PenPreset
    {
        public string Tool { get; set; } = "Pen";
        public string ColorHex { get; set; } = "#000000";
        public double Size { get; set; } = 2;
    }

    public sealed class LanguageOption
    {
        public LanguageOption(AppLanguage language, string displayName)
        {
            Language = language;
            DisplayName = displayName;
        }

        public AppLanguage Language { get; }

        public string DisplayName { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
