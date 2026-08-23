using System;
using System.Collections.Generic;

namespace Caelum.Models
{
    public class AnnotationData
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, PageAnnotation> Pages { get; set; } = new();
    }

    public class PageAnnotation
    {
        public List<StrokeAnnotation> Strokes { get; set; } = new();
        public List<TextAnnotation> Texts { get; set; } = new();
        public List<HighlightAnnotation> Highlights { get; set; } = new();
        public List<ImageAnnotation> Images { get; set; } = new();

        // Task 25: underline / strike-out / squiggly applied to PDF text.
        public List<TextMarkupAnnotation> TextMarkups { get; set; } = new();

        // Task 27: free-form rectangular area highlight (independent of the
        // PDF text layer).
        public List<AreaHighlightAnnotation> AreaHighlights { get; set; } = new();

        // Task 26: sticky notes (/Text annotations with editable content).
        public List<StickyNoteAnnotation> StickyNotes { get; set; } = new();

        // Study mode: opaque freehand masks that reveal briefly when clicked.
        // Hidden ink is intentionally separate from ordinary strokes so a
        // normal eraser or selection cannot accidentally change the answer.
        public List<HiddenInkAnnotation> HiddenInks { get; set; } = new();
    }

    public class StrokeAnnotation
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; } = 255;
        public double Size { get; set; } = 2.0;
        public bool IsHighlighter { get; set; }
        // Preserve the WPF rendering choice used by shape tools and smoothing.
        // Old sidecars omit this field and therefore keep the historical
        // FitToCurve=true behaviour.
        public bool FitToCurve { get; set; } = true;
        public List<double[]> Points { get; set; } = new();
    }

    /// <summary>
    /// A freehand opaque mask used by study mode. Coordinates are DIP (96dpi)
    /// with the page origin at the top-left, matching ordinary ink.
    /// Reveal state is transient UI state and is never serialized.
    /// </summary>
    public class HiddenInkAnnotation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public byte R { get; set; } = 255;
        public byte G { get; set; } = 255;
        public byte B { get; set; } = 255;
        public byte A { get; set; } = 255;
        public double Size { get; set; } = 28.0;
        public int RevealDurationMs { get; set; } = HiddenInkRevealState.DefaultRevealDurationMs;
        public List<double[]> Points { get; set; } = new();
    }

    public class TextAnnotation
    {
        public string Text { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public double FontSize { get; set; } = 18;
        // Width and Height were added after the original sidecar format. A zero
        // value deliberately means “automatic size” so old documents retain
        // their existing layout when loaded.
        public double Width { get; set; }
        public double Height { get; set; }
        // Task 29: rich-text properties are strings/enums in the portable
        // annotation model so JSON sidecars do not depend on WPF types.
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string FontFamily { get; set; } = "Segoe UI";
        public string Alignment { get; set; } = "Left";
    }

    public class HighlightAnnotation
    {
        // Each array contains [X, Y, Width, Height]
        public List<double[]> Rects { get; set; } = new();
        public byte R { get; set; } = 255;
        public byte G { get; set; } = 255;
        public byte B { get; set; } = 0;
        public byte A { get; set; } = 128;
    }

    /// <summary>
    /// Task 19: pasted/dropped image annotation. Coordinates are DIP (96dpi,
    /// Y from the page top) like every other annotation model. The raw encoded
    /// image bytes (PNG or JPEG) travel as base64 so save/load round-trips the
    /// original file without re-encoding.
    /// </summary>
    public class ImageAnnotation
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Format { get; set; } = "png"; // "png" or "jpeg"
        public string ImageDataBase64 { get; set; }
    }

    /// <summary>
    /// Task 25: kind of the text markup annotation. Serialized as the string
    /// name so JSON stays human-readable and the PDF subtype maps 1:1
    /// (/Underline, /StrikeOut, /Squiggly).
    /// </summary>
    public enum TextMarkupKind
    {
        Underline,
        StrikeOut,
        Squiggly
    }

    /// <summary>
    /// Task 25: underline / strike-out / squiggly markup over PDF text.
    /// X/Y is the bounding-box origin in page DIP coordinates; each rect in
    /// <see cref="Rects"/> is [dx, dy, w, h] relative to that origin. The
    /// container pipeline keeps X/Y in sync while the item is moved.
    /// </summary>
    public class TextMarkupAnnotation
    {
        public string Kind { get; set; } = nameof(TextMarkupKind.Underline);
        public double X { get; set; }
        public double Y { get; set; }
        public List<double[]> Rects { get; set; } = new();
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        public TextMarkupKind ParsedKind =>
            Enum.TryParse<TextMarkupKind>(Kind, true, out var kind)
                ? kind
                : TextMarkupKind.Underline;
    }

    /// <summary>
    /// Task 27: free-form rectangular area highlight (works over images and
    /// handwriting, not just the PDF text layer). Persisted as a /Highlight
    /// annotation with a single rectangular QuadPoints array and the
    /// wna_areahl_ /NM prefix so the loader can tell it apart from
    /// text-quad highlights.
    /// </summary>
    public class AreaHighlightAnnotation
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public byte R { get; set; } = 255;
        public byte G { get; set; } = 235;
        public byte B { get; set; } = 59;
        public byte A { get; set; } = 76; // ~30% opacity
    }

    /// <summary>
    /// Task 26: sticky note. A small collapsed icon placed on the page; the
    /// note text opens in an editing bubble. Persisted as a standard PDF
    /// /Text annotation (content in /Contents, position in /Rect) with the
    /// wna_note_ /NM prefix marking ownership.
    /// </summary>
    public class StickyNoteAnnotation
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Text { get; set; } = "";
    }
}
