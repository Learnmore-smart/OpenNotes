using System;
using System.IO;
using System.Linq;
using Caelum.Models;

namespace Caelum.Tests;

public sealed class ShapeRecognitionUndoTests
{
    [Test]
    public void Replacement_UndoRestoresOriginalAtOriginalIndex_WithoutAppending()
    {
        var state = StrokeReplacementFixture.Recognize(index: 1);

        Assert.That(state.Undo(), Is.True);
        Assert.That(state.Strokes.Count, Is.EqualTo(2));
        Assert.That(state.Strokes[1].Token, Is.EqualTo(state.Original.Token));
        Assert.That(state.Strokes[1].Snapshot, Is.EqualTo(state.Original));
    }

    [Test]
    public void Replacement_RedoAfterTheRestoredStrokeWasErased_IsNoOp()
    {
        var state = StrokeReplacementFixture.Recognize(index: 0);

        Assert.That(state.Undo(), Is.True);
        Assert.That(state.EraseToken(state.Original.Token), Is.True);
        Assert.That(state.Redo(), Is.False);
        Assert.That(state.Strokes, Is.Empty);
    }

    [Test]
    public void Replacement_UndoRedoSequenceNeverDuplicatesOrThrows()
    {
        var state = StrokeReplacementFixture.Recognize(index: 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.Undo(), Is.True);
            Assert.That(state.Undo(), Is.False);
            Assert.That(state.Redo(), Is.True);
            Assert.That(state.Redo(), Is.False);
            Assert.That(state.Strokes.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReplacementSource_UsesSnapshotsAndQuietNoAppendContract()
    {
        string root = FindProjectRoot();
        string editorCode = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        string pageCode = File.ReadAllText(Path.Combine(root, "Controls", "PdfPageControl.xaml.cs"));

        int actionStart = editorCode.IndexOf("class StrokeReplacedAction", StringComparison.Ordinal);
        int actionEnd = editorCode.IndexOf("class ItemsAddedAction", actionStart, StringComparison.Ordinal);
        int eventStart = pageCode.IndexOf("class StrokeRecognizedEventArgs", StringComparison.Ordinal);
        int eventEnd = pageCode.IndexOf("public sealed partial class PdfPageControl", eventStart, StringComparison.Ordinal);
        string action = actionStart >= 0 && actionEnd > actionStart
            ? editorCode.Substring(actionStart, actionEnd - actionStart)
            : string.Empty;
        string eventArgs = eventStart >= 0 && eventEnd > eventStart
            ? pageCode.Substring(eventStart, eventEnd - eventStart)
            : string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(actionStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(action, Does.Not.Contain("System.Windows.Ink.Stroke"));
            Assert.That(action, Does.Contain("_token"));
            Assert.That(action, Does.Contain("_originalSnapshot"));
            Assert.That(action, Does.Contain("_idealSnapshot"));
            Assert.That(eventArgs, Does.Not.Contain("System.Windows.Ink.Stroke"));
            Assert.That(pageCode, Does.Contain("bool ReplaceRecognizedStroke"));
            Assert.That(pageCode, Does.Not.Contain("_strokes.Add(ideal)"));
        });
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
    }

    private sealed class StrokeReplacementFixture
    {
        private readonly StrokeReplacementState _state;
        private readonly StrokeReplacementSnapshot _ideal;

        private StrokeReplacementFixture(
            StrokeReplacementState state,
            StrokeReplacementSnapshot original,
            StrokeReplacementSnapshot ideal)
        {
            _state = state;
            Original = original;
            _ideal = ideal;
        }

        public StrokeReplacementSnapshot Original { get; }

        public System.Collections.Generic.IReadOnlyList<StrokeReplacementEntry> Strokes => _state.Strokes;

        public static StrokeReplacementFixture Recognize(int index)
        {
            var originalToken = Guid.NewGuid();
            var original = Snapshot(originalToken, StrokeReplacementSide.Original, index + 10);
            var ideal = Snapshot(originalToken, StrokeReplacementSide.Ideal, index + 40);
            var entries = new System.Collections.Generic.List<StrokeReplacementEntry>();
            if (index > 0)
            {
                entries.Add(new StrokeReplacementEntry(
                    Snapshot(Guid.NewGuid(), StrokeReplacementSide.Original, index + 20)));
            }
            entries.Add(new StrokeReplacementEntry(original));
            var state = new StrokeReplacementState(entries);

            Assert.That(state.TryReplaceStrokeQuiet(originalToken, StrokeReplacementSide.Original, ideal, out var replacedIndex), Is.True);
            Assert.That(replacedIndex, Is.EqualTo(index));
            return new StrokeReplacementFixture(state, original, ideal);
        }

        public bool Undo() => _state.TryReplaceStrokeQuiet(
            Original.Token,
            StrokeReplacementSide.Ideal,
            Original,
            out _);

        public bool Redo() => _state.TryReplaceStrokeQuiet(
            Original.Token,
            StrokeReplacementSide.Original,
            _ideal,
            out _);

        public bool EraseToken(Guid token) => _state.RemoveToken(token);

        private static StrokeReplacementSnapshot Snapshot(Guid token, StrokeReplacementSide side, double offset)
        {
            return new StrokeReplacementSnapshot(
                token,
                side,
                new[]
                {
                    new StrokeReplacementPoint(offset, offset + 1),
                    new StrokeReplacementPoint(offset + 10, offset + 11)
                },
                r: 12,
                g: 34,
                b: 56,
                a: 255,
                width: 2.5,
                height: 2.5,
                isHighlighter: false,
                fitToCurve: side == StrokeReplacementSide.Original);
        }
    }
}
