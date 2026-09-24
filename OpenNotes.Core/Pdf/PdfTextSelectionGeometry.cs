using System;
using System.Collections.Generic;
using Caelum.InkGeometry;
using Caelum.Models;

namespace Caelum.Pdf
{
    /// <summary>
    /// UI-free port of the WPF editor's PDF-text-selection math
    /// (<c>EditorPage.FindNearestTextOffset</c> /
    /// <c>BuildPdfTextSelectionRects</c> / <c>BuildTextMarkupAnnotation</c>
    /// and <c>PdfPageControl.NormalizeAreaHighlightRect</c>), plus the image
    /// fit rule used by <c>AddImage</c>. Everything runs on the page-DIP
    /// coordinate space the renderers and annotation models share, so the
    /// WinUI page control and headless tests drive the identical logic.
    /// </summary>
    public static class PdfTextSelectionGeometry
    {
        /// <summary>
        /// The containment padding WPF applies before measuring distances —
        /// a press within 3 DIP of a glyph box snaps straight to that glyph
        /// instead of losing to a numerically closer neighbour.
        /// </summary>
        private const double ContainmentPaddingDips = 3.0;

        /// <summary>
        /// Two adjacent glyph rects on the same visual line merge into one
        /// selection rectangle when their vertical overlap covers at least
        /// 35% of the shorter height and the horizontal gap stays under
        /// 8 DIP (WPF ShouldMergeSelectionRects). The merge keeps the quad
        /// look the WPF overlay paints instead of a rect per glyph.
        /// </summary>
        private const double MergeVerticalOverlapRatio = 0.35;
        private const double MergeMaxHorizontalGap = 8.0;

        /// <summary>Padded containment check (WPF RectContainsWithPadding).</summary>
        public static bool RectContainsWithPadding(RectD rect, PointD point, double padding)
            => rect.Inflated(padding, padding).Contains(point);

        /// <summary>Edge-distance between a point and a rect (0 inside — WPF DistanceToRect).</summary>
        public static double DistanceToRect(PointD point, RectD rect)
        {
            double dx = 0;
            if (point.X < rect.Left)
                dx = rect.Left - point.X;
            else if (point.X > rect.Right)
                dx = point.X - rect.Right;

            double dy = 0;
            if (point.Y < rect.Top)
                dy = rect.Top - point.Y;
            else if (point.Y > rect.Bottom)
                dy = point.Y - rect.Bottom;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Nearest character offset for a page point, WPF parity: a hit
        /// inside a glyph box padded by <see cref="ContainmentPaddingDips"/>
        /// wins immediately; otherwise the closest union-bounds rect under
        /// <paramref name="maxDistance"/> answers (positive-infinity = always
        /// answer — the drag path follows the pointer anywhere on the page).
        /// Characters with no bounds never match.
        /// </summary>
        public static int FindNearestTextOffset(
            PdfService.PdfPageTextInfo textInfo, PointD point, double maxDistance)
        {
            if (textInfo?.Characters == null || textInfo.Characters.Count == 0)
                return -1;

            int bestOffset = -1;
            double bestDistance = double.MaxValue;

            foreach (var character in textInfo.Characters)
            {
                if (character.Bounds == null || character.Bounds.Count == 0
                    || character.UnionBounds.Width <= 0 || character.UnionBounds.Height <= 0)
                    continue;

                if (RectContainsWithPadding(character.UnionBounds, point, ContainmentPaddingDips))
                    return character.Offset;

                double distance = DistanceToRect(point, character.UnionBounds);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestOffset = character.Offset;
                }
            }

            return bestDistance <= maxDistance ? bestOffset : -1;
        }

        /// <summary>
        /// Merge rule for adjacent glyph rects (WPF ShouldMergeSelectionRects):
        /// same visual line (vertical overlap ≥ 35% of the shorter height)
        /// AND close enough horizontally (next left edge within 8 DIP of the
        /// current right edge).
        /// </summary>
        public static bool ShouldMergeSelectionRects(RectD current, RectD next)
        {
            double verticalOverlap = Math.Min(current.Bottom, next.Bottom)
                - Math.Max(current.Top, next.Top);
            bool sameLine = verticalOverlap >= Math.Min(current.Height, next.Height)
                * MergeVerticalOverlapRatio;
            bool closeEnough = next.Left <= current.Right + MergeMaxHorizontalGap;
            return sameLine && closeEnough;
        }

        /// <summary>
        /// Builds the merged selection rectangles covering text offsets
        /// [startOffset..endOffset] (order-independent; clamped to the
        /// character list). Characters contribute every bound they carry —
        /// pdfium can split one glyph across multiple rects — and adjacent
        /// same-line fragments merge via <see cref="ShouldMergeSelectionRects"/>.
        /// </summary>
        public static IReadOnlyList<RectD> BuildSelectionRects(
            PdfService.PdfPageTextInfo textInfo, int startOffset, int endOffset)
        {
            var mergedRects = new List<RectD>();
            if (textInfo?.Characters == null || textInfo.Characters.Count == 0)
                return mergedRects;

            int start = Math.Max(0, Math.Min(startOffset, endOffset));
            int end = Math.Min(textInfo.Characters.Count - 1, Math.Max(startOffset, endOffset));

            for (int i = start; i <= end; i++)
            {
                var character = textInfo.Characters[i];
                if (character.Bounds == null || character.Bounds.Count == 0)
                    continue;

                foreach (var rect in character.Bounds)
                {
                    if (rect.Width <= 0 || rect.Height <= 0)
                        continue;

                    if (mergedRects.Count > 0
                        && ShouldMergeSelectionRects(mergedRects[mergedRects.Count - 1], rect))
                    {
                        var merged = mergedRects[mergedRects.Count - 1];
                        mergedRects[mergedRects.Count - 1] = merged.Union(rect);
                    }
                    else
                    {
                        mergedRects.Add(rect);
                    }
                }
            }

            return mergedRects;
        }

        /// <summary>
        /// Builds the underline/strike-out/squiggly model from absolute
        /// page rects (WPF BuildTextMarkupAnnotation): the union bounds
        /// becomes the annotation origin and each rect relativises to it.
        /// An empty input still returns a model at (0,0) with no rects.
        /// </summary>
        public static TextMarkupAnnotation BuildTextMarkupAnnotation(
            IReadOnlyList<RectD> absoluteRects,
            TextMarkupKind kind,
            byte r, byte g, byte b)
        {
            RectD bounds = default;
            bool hasBounds = false;
            foreach (var rect in absoluteRects ?? Array.Empty<RectD>())
            {
                if (rect.Width <= 0 || rect.Height <= 0)
                    continue;
                bounds = hasBounds ? bounds.Union(rect) : rect;
                hasBounds = true;
            }

            var markup = new TextMarkupAnnotation
            {
                Kind = kind.ToString(),
                X = hasBounds ? bounds.X : 0,
                Y = hasBounds ? bounds.Y : 0,
                R = r,
                G = g,
                B = b,
            };

            if (!hasBounds)
                return markup;

            foreach (var rect in absoluteRects)
            {
                if (rect.Width > 0 && rect.Height > 0)
                {
                    markup.Rects.Add(new[]
                    {
                        rect.X - bounds.X,
                        rect.Y - bounds.Y,
                        rect.Width,
                        rect.Height,
                    });
                }
            }

            return markup;
        }

        /// <summary>
        /// Area-highlight drag normalization (WPF
        /// <c>PdfPageControl.NormalizeAreaHighlightRect</c>): the committed
        /// rectangle is the axis-aligned box between anchor and current
        /// regardless of drag direction.
        /// </summary>
        public static RectD NormalizeAreaHighlightRect(PointD anchor, PointD current)
            => new(
                Math.Min(anchor.X, current.X),
                Math.Min(anchor.Y, current.Y),
                Math.Abs(current.X - anchor.X),
                Math.Abs(current.Y - anchor.Y));

        /// <summary>
        /// The WPF <c>AddImage</c> fit rule: explicit dimensions win
        /// verbatim; otherwise the image is scaled inside 40% of the page
        /// keeping aspect, with a 1-DIP floor. Returns the placed size.
        /// </summary>
        public static (double Width, double Height) ComputeImagePlacementSize(
            double pageWidth, double pageHeight,
            double imagePixelWidth, double imagePixelHeight,
            double? explicitWidth, double? explicitHeight)
        {
            if (explicitWidth > 0 && explicitHeight > 0)
                return (explicitWidth.Value, explicitHeight.Value);

            double maxW = pageWidth * 0.4;
            double maxH = pageHeight * 0.4;
            if (imagePixelWidth <= 0 || imagePixelHeight <= 0 || maxW <= 0 || maxH <= 0)
                return (1.0, 1.0);
            double fit = Math.Min(maxW / imagePixelWidth, maxH / imagePixelHeight);
            return (Math.Max(1.0, imagePixelWidth * fit), Math.Max(1.0, imagePixelHeight * fit));
        }
    }
}
