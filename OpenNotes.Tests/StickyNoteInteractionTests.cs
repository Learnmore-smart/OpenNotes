using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;
using PdfSharpCore.Pdf;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class StickyNoteInteractionTests
{
    [Test]
    public void StickyNoteRoundTripKeepsIdentityDipPositionAndText()
    {
        var note = new StickyNoteAnnotation
        {
            X = 32,
            Y = 48,
            Text = "English / 便签 / note",
            Width = 44,
            Height = 40,
            R = 20,
            G = 120,
            B = 220,
        };

        var restored = JsonSerializer.Deserialize<StickyNoteAnnotation>(
            JsonSerializer.Serialize(note));

        Assert.That(restored, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(restored!.Id, Is.EqualTo(note.Id));
            Assert.That(restored.X, Is.EqualTo(32));
            Assert.That(restored.Y, Is.EqualTo(48));
            Assert.That(restored.Text, Is.EqualTo("English / 便签 / note"));
            Assert.That(restored.Width, Is.EqualTo(note.Width));
            Assert.That(restored.Height, Is.EqualTo(note.Height));
            Assert.That(restored.R, Is.EqualTo(note.R));
            Assert.That(restored.G, Is.EqualTo(note.G));
            Assert.That(restored.B, Is.EqualTo(note.B));
        });
    }

    [Test]
    public void StickyNotePositionClampsToMeasuredPageBounds()
    {
        var clamped = PdfPageControl.ClampStickyNotePosition(
            new Point(990, 790),
            new Size(1000, 800),
            new Size(36, 36));

        Assert.That(clamped.X, Is.EqualTo(964).Within(0.001));
        Assert.That(clamped.Y, Is.EqualTo(764).Within(0.001));
    }

    [Test]
    public async Task StickyNotePdfRoundTripKeepsIdentityGeometryColourAndCjkText()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opennotes-sticky-{Guid.NewGuid():N}.pdf");
        PdfService service = null!;
        try
        {
            using (var document = new PdfDocument())
            {
                document.AddPage();
                document.Save(path);
            }

            var note = new StickyNoteAnnotation
            {
                Id = "sticky-pdf-identity",
                X = 96,
                Y = 144,
                Text = "English / 便签 / note",
                Width = 48,
                Height = 40,
                R = 24,
                G = 128,
                B = 220
            };
            service = new PdfService();
            await service.SaveAnnotationsToPdfAsync(path,
                new Dictionary<int, PageAnnotation>
                {
                    [0] = new PageAnnotation { StickyNotes = new List<StickyNoteAnnotation> { note } }
                });
            await service.LoadPdfAsync(path);

            var restored = service.ExtractedAnnotations[0].StickyNotes.Single();
            Assert.Multiple(() =>
            {
                Assert.That(restored.Id, Is.EqualTo(note.Id));
                Assert.That(restored.X, Is.EqualTo(note.X).Within(0.01));
                Assert.That(restored.Y, Is.EqualTo(note.Y).Within(0.01));
                Assert.That(restored.Text, Is.EqualTo(note.Text));
                Assert.That(restored.Width, Is.EqualTo(note.Width).Within(0.01));
                Assert.That(restored.Height, Is.EqualTo(note.Height).Within(0.01));
                Assert.That(restored.R, Is.EqualTo(note.R));
                Assert.That(restored.G, Is.EqualTo(note.G));
                Assert.That(restored.B, Is.EqualTo(note.B));
            });
        }
        finally
        {
            if (service != null)
                await service.DisposeAsync();
            try { File.Delete(path); } catch { }
        }
    }

    [Test]
    public async Task StickyNotePdfRoundTripRepairsDuplicateAndEmptyIdsWithoutDroppingPayload()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opennotes-sticky-duplicates-{Guid.NewGuid():N}.pdf");
        PdfService service = null!;
        try
        {
            using (var document = new PdfDocument())
            {
                document.AddPage();
                document.Save(path);
            }

            var notes = new List<StickyNoteAnnotation>
            {
                new() { Id = "duplicate-id", X = 12, Y = 18, Text = "first / 一", R = 1, G = 2, B = 3 },
                new() { Id = "duplicate-id", X = 42, Y = 58, Text = "second / deux", R = 4, G = 5, B = 6 },
                new() { Id = " ", X = 72, Y = 98, Text = "third / troisième", R = 7, G = 8, B = 9 }
            };

            service = new PdfService();
            await service.SaveAnnotationsToPdfAsync(
                path,
                new Dictionary<int, PageAnnotation>
                {
                    [0] = new PageAnnotation { StickyNotes = notes }
                });
            await service.LoadPdfAsync(path);

            var restored = service.ExtractedAnnotations[0].StickyNotes;
            Assert.That(restored, Has.Count.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(restored.Select(note => note.Id), Is.Unique);
                Assert.That(restored.All(note => !string.IsNullOrWhiteSpace(note.Id)), Is.True);
                Assert.That(restored.Select(note => note.Text), Is.EquivalentTo(notes.Select(note => note.Text)));
                Assert.That(restored.Select(note => note.R), Is.EquivalentTo(new byte[] { 1, 4, 7 }));
            });
        }
        finally
        {
            if (service != null)
                await service.DisposeAsync();
            try { File.Delete(path); } catch { }
        }
    }

    [Test]
    public void StickyNoteSourceMakesOnlyMarkersInteractive()
    {
        var xaml = ReadProjectFile("Controls", "PdfPageControl.xaml");
        var source = ReadProjectFile("Controls", "PdfPageControl.xaml.cs");
        var editorSource = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Name=\"ImageOverlayCanvas\""));
            Assert.That(xaml, Does.Contain("Background=\"{x:Null}\""));
            Assert.That(xaml, Does.Contain("IsHitTestVisible=\"True\""));
            Assert.That(source, Does.Contain("container.CaptureMouse();"));
            Assert.That(source, Does.Contain("StickyNoteMovedEventArgs"));
            Assert.That(source, Does.Contain("SetStickyNotePositionQuiet"));
            Assert.That(source, Does.Contain("AutomationProperties.SetAutomationId"));
            Assert.That(source, Does.Contain("StickyNoteDeleteRequested"));
            Assert.That(source, Does.Contain("StickyNoteContextMenuCreated"));
            Assert.That(source, Does.Contain("KeyboardNavigation.SetIsTabStop"));
            Assert.That(source, Does.Contain("StickyNote_KeyDown"));
            Assert.That(source, Does.Contain("BuildStickyNoteContextMenu"));
            Assert.That(source, Does.Contain("note.Width = container.Width"));
            Assert.That(source, Does.Contain("SetStickyNotePositionQuiet(container, new Point(newLeft, newTop))"));
            Assert.That(source, Does.Contain("IInteractionCancellation"));
            Assert.That(source, Does.Contain("LostMouseCapture"));
            Assert.That(source, Does.Contain("LostStylusCapture"));
            Assert.That(source, Does.Contain("CancelInteraction"));
            Assert.That(source, Does.Contain("EnsureStickyNoteIdentity"));
            Assert.That(source, Does.Contain("FocusVisualStyle"));
            Assert.That(editorSource, Does.Contain("TransferOverlayData"));
            Assert.That(editorSource, Does.Contain("CancelTextBoxDrag"));
            Assert.That(editorSource, Does.Contain("CancelTextResize"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ProductionStaStickyCaptureCancellationRollsBackAndCanReopen()
    {
        EnsureWpfEnvironment();
        CreateTestApplication();

        var page = new PdfPageControl { Width = 240, Height = 240 };
        var note = new StickyNoteAnnotation
        {
            Id = "sticky-capture-cancel",
            X = 32,
            Y = 48,
            Width = 40,
            Height = 40,
            Text = "capture / 捕获 / capture"
        };
        var container = page.AddStickyNote(note);
        Assert.That(container, Is.Not.Null);

        var moved = 0;
        var activated = 0;
        page.StickyNoteMoved += (_, _) => moved++;
        page.StickyNoteActivated += (_, _) => activated++;

        InvokePrivate(page, "BeginStickyPointer", container!, new Point(note.X, note.Y), false);
        InvokePrivate(page, "UpdateStickyPointer", container!, new Point(180, 180));
            page.CancelInteraction("lost mouse capture");

        Assert.Multiple(() =>
        {
            Assert.That(note.X, Is.EqualTo(32).Within(0.001));
            Assert.That(note.Y, Is.EqualTo(48).Within(0.001));
            Assert.That(moved, Is.EqualTo(0));
            Assert.That(activated, Is.EqualTo(0));
        });

        // A cancelled capture must not poison the next click/activation.
        InvokePrivate(page, "BeginStickyPointer", container!, new Point(note.X, note.Y), false);
        InvokePrivate(page, "EndStickyPointer", container!, false);
        Assert.That(activated, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ProductionStaStickyIdsAreUniquePerPageWithoutLosingPayload()
    {
        EnsureWpfEnvironment();
        CreateTestApplication();

        var page = new PdfPageControl { Width = 320, Height = 320 };
        var first = new StickyNoteAnnotation
        {
            Id = "duplicate-id", X = 12, Y = 18, Text = "first", R = 1, G = 2, B = 3
        };
        var duplicate = new StickyNoteAnnotation
        {
            Id = "duplicate-id", X = 42, Y = 58, Text = "second / 第二", R = 4, G = 5, B = 6
        };
        var empty = new StickyNoteAnnotation
        {
            Id = " ", X = 72, Y = 98, Text = "third / troisième", R = 7, G = 8, B = 9
        };

        page.AddStickyNote(first);
        page.AddStickyNote(duplicate);
        page.AddStickyNote(empty);

        var notes = page.GetOverlayContainers()
            .Select(container => page.GetOverlayData(container))
            .OfType<StickyNoteAnnotation>()
            .ToList();
        Assert.That(notes, Has.Count.EqualTo(3));
        Assert.That(notes.Select(note => note.Id), Is.Unique);
        Assert.That(notes.All(note => !string.IsNullOrWhiteSpace(note.Id)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(notes.Select(note => note.Text), Does.Contain("first"));
            Assert.That(notes.Select(note => note.Text), Does.Contain("second / 第二"));
            Assert.That(notes.Select(note => note.Text), Does.Contain("third / troisième"));
            Assert.That(notes.Single(note => note.Text == "second / 第二").R, Is.EqualTo(4));
        });
    }

    [Test]
    public void StickyEditorExposesExplicitSaveCancelDeleteAndCancelOnClose()
    {
        var source = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        var controlSource = ReadProjectFile("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Sticky.Save"));
            Assert.That(source, Does.Contain("Sticky.Cancel"));
            Assert.That(source, Does.Contain("Sticky.Delete"));
            Assert.That(source, Does.Contain("CancelStickyNoteEdit();"));
            Assert.That(source, Does.Contain("StickyNoteMoved"));
            Assert.That(source, Does.Contain("StickyNoteDeleted"));
            Assert.That(controlSource, Does.Contain("Sticky.Delete.ContextMenu"));
            Assert.That(controlSource, Does.Contain("StickyNoteContextMenuLocalization"));
        });
    }

    [Test]
    public void StickyEditorRefreshesLocalizedActionMetadataAndSharedFocusCue()
    {
        var source = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        var utilities = ReadProjectFile("Pages", "EditorPage.Utilities.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ApplyStickyNoteButtonMetadata"));
            Assert.That(source, Does.Contain("ApplyToolbarFocusVisualStyle"));
            Assert.That(source, Does.Contain("ThemeFocusBrush"));
            Assert.That(source, Does.Contain("new Thickness(2)"));
            Assert.That(utilities, Does.Contain("ApplyStickyNoteButtonMetadata"));
            Assert.That(utilities, Does.Contain("ToolTipService.SetToolTip"));
            Assert.That(utilities, Does.Contain("AutomationProperties.SetName"));
            Assert.That(utilities, Does.Contain("AutomationProperties.SetHelpText"));
        });
    }

    [Test]
    public void StickyEditorUsesSemanticRoundedActionsAndAnExplicitDragSurface()
    {
        var source = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DialogPrimaryButton"));
            Assert.That(source, Does.Contain("DialogSecondaryButton"));
            Assert.That(source, Does.Contain("ThemeDangerBrush"));
            Assert.That(source, Does.Contain("_stickyNoteDragHandle"));
            Assert.That(source, Does.Contain("Cursors.SizeAll"));
            Assert.That(source, Does.Contain("Sticky.Editor.DragHandle"));
            Assert.That(source, Does.Contain("BeginStickyNotePopupDrag"));
            Assert.That(source, Does.Contain("UpdateStickyNotePopupDrag"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void StickyEditorHeaderDragMovesOnlyTheTransientPopup()
    {
        EnsureWpfEnvironment();
        CreateTestApplication();

        var editor = new EditorPage();
        var page = new PdfPageControl { Width = 300, Height = 300 };
        GetPrivateField<List<PdfPageControl>>(editor, "_pageControls").Add(page);
        var note = new StickyNoteAnnotation { Id = "sticky-drag-editor", X = 40, Y = 48, Text = "move me" };
        var container = page.AddStickyNote(note);
        Assert.That(container, Is.Not.Null);

        InvokePrivate(editor, "OpenStickyNoteEditor", page, container!, note);
        var popup = GetPrivateField<Popup>(editor, "_stickyNotePopup");
        var dragHandle = GetPrivateField<Border>(editor, "_stickyNoteDragHandle");
        double originalX = note.X;
        double originalY = note.Y;

        InvokePrivate(editor, "BeginStickyNotePopupDrag", new Point(10, 10));
        InvokePrivate(editor, "UpdateStickyNotePopupDrag", new Point(54, 38));
        InvokePrivate(editor, "EndStickyNotePopupDrag");

        Assert.Multiple(() =>
        {
            Assert.That(popup.HorizontalOffset, Is.EqualTo(44).Within(0.01));
            Assert.That(popup.VerticalOffset, Is.EqualTo(28).Within(0.01));
            Assert.That(note.X, Is.EqualTo(originalX));
            Assert.That(note.Y, Is.EqualTo(originalY));
            Assert.That(editor.IsDirty, Is.False);
            Assert.That(dragHandle.Cursor, Is.EqualTo(Cursors.SizeAll));
            Assert.That(AutomationProperties.GetAutomationId(dragHandle), Is.EqualTo("Sticky.Editor.DragHandle"));
        });

        editor.CloseTransientUi("sticky drag cleanup");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ProductionStaStaleStickyEditorCannotMutateNewDocumentSession()
    {
        EnsureWpfEnvironment();
        CreateTestApplication();

        var editor = new EditorPage();
        var page = new PdfPageControl { Width = 300, Height = 300 };
        GetPrivateField<List<PdfPageControl>>(editor, "_pageControls").Add(page);
        var note = new StickyNoteAnnotation { Id = "stale-session", Text = "before", X = 24, Y = 30 };
        var container = page.AddStickyNote(note);
        Assert.That(container, Is.Not.Null);

        InvokePrivate(editor, "OpenStickyNoteEditor", page, container!, note);
        var textBox = GetPrivateField<TextBox>(editor, "_stickyNoteEditor");
        textBox.Text = "must-not-commit";
        var loadSession = typeof(EditorPage).GetField("_loadSessionId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        loadSession.SetValue(editor, (int)loadSession.GetValue(editor)! + 1);

        InvokePrivate(editor, "SaveStickyNoteEdit");

        Assert.Multiple(() =>
        {
            Assert.That(note.Text, Is.EqualTo("before"));
            Assert.That(page.GetOverlayContainers(), Does.Contain(container));
            Assert.That(editor.IsDirty, Is.False);
            Assert.That(GetPrivateField<System.Collections.IList>(editor, "_undoStack"), Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ProductionStaTextDragAndResizeCancelOnCaptureLossOrDeactivate()
    {
        EnsureWpfEnvironment();
        CreateTestApplication();

        var editor = new EditorPage();
        var page = new PdfPageControl { Width = 360, Height = 360 };
        GetPrivateField<List<PdfPageControl>>(editor, "_pageControls").Add(page);
        var toolField = typeof(EditorPage).GetField("_currentTool", BindingFlags.Instance | BindingFlags.NonPublic)!;
        toolField.SetValue(editor, Enum.Parse(toolField.FieldType, "Text"));

        var createTextBox = typeof(EditorPage).GetMethod("CreateTextBox", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var container = (Grid)createTextBox.Invoke(editor, new object?[]
        {
            page, new Point(40, 44), null, null, "gesture", false, false,
            null, null, null, null, null, null
        })!;
        var dragHandle = container.Children.OfType<Border>().Single(border => border.Cursor == Cursors.SizeAll);
        double originalX = Canvas.GetLeft(container);
        double originalY = Canvas.GetTop(container);

        InvokePrivate(editor, "BeginTextBoxDrag", dragHandle, new Point(originalX, originalY));
        InvokePrivate(editor, "UpdateTextBoxDrag", new Point(originalX + 72, originalY + 54), new Action(() => { }));
        InvokePrivate(editor, "DragHandle_LostMouseCapture", dragHandle, null);

        var resizeHandle = container.Children.OfType<Border>().First(border => border.Tag is TextResizeHandle);
        var resizeKind = (TextResizeHandle)resizeHandle.Tag;
        double originalWidth = container.Width;
        double originalHeight = container.Height;
        InvokePrivate(editor, "BeginTextResize", resizeHandle, container, page, resizeKind, new Point(originalX, originalY));
        InvokePrivate(editor, "UpdateTextResize", new Point(originalX + 36, originalY + 28));
        editor.SetHostActive(false);

        Assert.Multiple(() =>
        {
            Assert.That(Canvas.GetLeft(container), Is.EqualTo(originalX).Within(0.001));
            Assert.That(Canvas.GetTop(container), Is.EqualTo(originalY).Within(0.001));
            Assert.That(container.Width, Is.EqualTo(originalWidth).Within(0.001));
            Assert.That(container.Height, Is.EqualTo(originalHeight).Within(0.001));
            Assert.That(editor.IsDirty, Is.False);
            Assert.That(GetPrivateField<System.Collections.IList>(editor, "_undoStack"), Is.Empty);
            Assert.That(editor.HasActiveInteraction, Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ProductionStaStickyPopupRefreshesAllMetadataAcrossEnglishChineseFrench()
    {
        EnsureWpfEnvironment();
        CreateTestApplication();
        var previousLanguage = LocalizationService.CurrentLanguage;
        try
        {
            LocalizationService.ApplyLanguage(AppLanguage.English);
            var editor = new EditorPage();
            var page = new PdfPageControl { Width = 300, Height = 300 };
            GetPrivateField<List<PdfPageControl>>(editor, "_pageControls").Add(page);
            var note = new StickyNoteAnnotation { Id = "localized-popup", Text = "text" };
            var container = page.AddStickyNote(note);
            InvokePrivate(editor, "OpenStickyNoteEditor", page, container!, note);

            var save = GetPrivateField<Button>(editor, "_stickyNoteSaveButton");
            var cancel = GetPrivateField<Button>(editor, "_stickyNoteCancelButton");
            var delete = GetPrivateField<Button>(editor, "_stickyNoteDeleteButton");
            foreach (var language in new[] { AppLanguage.English, AppLanguage.Chinese, AppLanguage.French })
            {
                LocalizationService.ApplyLanguage(language);
                editor.ApplyLocalization();
                AssertStickyButtonLocalization(save, "Common.Save", "Sticky.Save");
                AssertStickyButtonLocalization(cancel, "Common.Cancel", "Sticky.Cancel");
                AssertStickyButtonLocalization(delete, "Editor.DeleteTooltip", "Sticky.Delete");
            }

            editor.CloseTransientUi("localized popup test");
        }
        finally
        {
            LocalizationService.ApplyLanguage(previousLanguage);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ProductionStaStickyMarkerSupportsDragKeyboardClampAndUiA()
    {
        EnsureWpfEnvironment();
        CreateTestApplication();

        var page = new PdfPageControl { Width = 200, Height = 200 };
        var note = new StickyNoteAnnotation
        {
            Id = "sticky-sta-marker",
            X = 100,
            Y = 100,
            Width = 36,
            Height = 36,
            Text = "STA / 便签 / note"
        };
        var container = page.AddStickyNote(note);
        Assert.That(container, Is.Not.Null);

        var moved = new List<StickyNoteMovedEventArgs>();
        bool deleteRequested = false;
        page.StickyNoteMoved += (_, args) => moved.Add(args);
        page.StickyNoteDeleteRequested += (_, _) => deleteRequested = true;

        InvokePrivate(page, "BeginStickyPointer", container!, new Point(100, 100), false);
        InvokePrivate(page, "UpdateStickyPointer", container!, new Point(500, 500));
        InvokePrivate(page, "EndStickyPointer", container!, false);

        Assert.Multiple(() =>
        {
            Assert.That(note.X, Is.EqualTo(164).Within(0.001));
            Assert.That(note.Y, Is.EqualTo(164).Within(0.001));
            Assert.That(moved, Has.Count.EqualTo(1));
            Assert.That(container!.IsHitTestVisible, Is.True);
            Assert.That(container.Focusable, Is.True);
            Assert.That(KeyboardNavigation.GetIsTabStop(container), Is.True);
            Assert.That(AutomationProperties.GetAutomationId(container), Is.EqualTo("StickyNote.sticky-sta-marker"));
            Assert.That(AutomationProperties.GetName(container), Is.EqualTo(LocalizationService.Get("Editor.StickyNoteTooltip")));
            Assert.That(AutomationProperties.GetHelpText(container), Is.EqualTo(LocalizationService.Get("Editor.StickyNoteTooltip")));
            Assert.That(ToolTipService.GetToolTip(container), Is.EqualTo(note.Text));
        });

        var keyHandler = typeof(PdfPageControl).GetMethod(
            "StickyNote_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var host = new Window { Content = page, Width = 240, Height = 240 };
        host.Show();
        host.UpdateLayout();
        try
        {
            var inputSource = PresentationSource.FromVisual(container!);
            Assert.That(inputSource, Is.Not.Null);
            var left = new KeyEventArgs(Keyboard.PrimaryDevice, inputSource!, 0, Key.Left)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            keyHandler.Invoke(page, new object[] { container!, left });
            Assert.That(note.X, Is.EqualTo(160).Within(0.001));

            var delete = container!.ContextMenu!.Items.OfType<MenuItem>().Single();
            var deletePeer = UIElementAutomationPeer.CreatePeerForElement(delete);
            Assert.That(deletePeer, Is.Not.Null);
            Assert.That(deletePeer!.GetPattern(PatternInterface.Invoke), Is.Not.Null);

            var deleteKey = new KeyEventArgs(Keyboard.PrimaryDevice, inputSource!, 0, Key.Delete)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            keyHandler.Invoke(page, new object[] { container, deleteKey });
            Assert.That(deleteRequested, Is.True);
        }
        finally
        {
            host.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ProductionStaStickyEditorSaveCancelDeleteAndUndoRedoUseLiveControls()
    {
        EnsureWpfEnvironment();
        CreateTestApplication();

        var editor = new EditorPage();
        var page = new PdfPageControl { Width = 300, Height = 300 };
        GetPrivateField<List<PdfPageControl>>(editor, "_pageControls").Add(page);

        var note = new StickyNoteAnnotation
        {
            Id = "sticky-sta-editor",
            X = 40,
            Y = 48,
            Text = "before",
            Width = 40,
            Height = 40
        };
        var container = page.AddStickyNote(note);
        Assert.That(container, Is.Not.Null);

        var activated = typeof(EditorPage).GetMethod(
            "PageControl_StickyNoteActivated", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var moved = typeof(EditorPage).GetMethod(
            "PageControl_StickyNoteMoved", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var deleted = typeof(EditorPage).GetMethod(
            "PageControl_StickyNoteDeleteRequested", BindingFlags.Instance | BindingFlags.NonPublic)!;
        page.StickyNoteActivated += (sender, item) => activated.Invoke(editor, new object?[] { sender, item });
        page.StickyNoteMoved += (sender, args) => moved.Invoke(editor, new object?[] { sender, args });
        page.StickyNoteDeleteRequested += (sender, item) => deleted.Invoke(editor, new object?[] { sender, item });

        // Raise the real marker activation path: the control's pointer state
        // emits StickyNoteActivated, and EditorPage builds the live Popup.
        InvokePrivate(page, "BeginStickyPointer", container!, new Point(40, 48), false);
        InvokePrivate(page, "EndStickyPointer", container!, false);

        var popup = GetPrivateField<Popup>(editor, "_stickyNotePopup");
        var textBox = GetPrivateField<TextBox>(editor, "_stickyNoteEditor");
        var save = GetPrivateField<Button>(editor, "_stickyNoteSaveButton");
        var cancel = GetPrivateField<Button>(editor, "_stickyNoteCancelButton");
        var delete = GetPrivateField<Button>(editor, "_stickyNoteDeleteButton");
        Assert.That(popup.IsOpen, Is.True);
        AssertStickyButton(save, "Sticky.Save");
        AssertStickyButton(cancel, "Sticky.Cancel");
        AssertStickyButton(delete, "Sticky.Delete");

        textBox.Text = "after-save";
        InvokePrivate(editor, "SaveStickyNoteEdit");
        Assert.That(note.Text, Is.EqualTo("after-save"));

        InvokePrivateAsync(editor, "PerformUndoAsync");
        Assert.That(note.Text, Is.EqualTo("before"));
        InvokePrivateAsync(editor, "PerformRedoAsync");
        Assert.That(note.Text, Is.EqualTo("after-save"));

        // Reopen, mutate the live TextBox, then close through the global
        // transient contract.  CloseTransientUi is intentionally Cancel.
        InvokePrivate(page, "BeginStickyPointer", container!, new Point(40, 48), false);
        InvokePrivate(page, "EndStickyPointer", container!, false);
        textBox = GetPrivateField<TextBox>(editor, "_stickyNoteEditor");
        textBox.Text = "discarded";
        editor.CloseTransientUi("STA test");
        Assert.That(note.Text, Is.EqualTo("after-save"));

        // Explicit Delete removes the live marker and leaves a reversible
        // action that restores the same model/container identity.
        InvokePrivate(page, "BeginStickyPointer", container!, new Point(40, 48), false);
        InvokePrivate(page, "EndStickyPointer", container!, false);
        InvokePrivate(editor, "DeleteStickyNoteEdit");
        Assert.That(page.GetOverlayContainers(), Is.Empty);
        InvokePrivateAsync(editor, "PerformUndoAsync");
        Assert.That(page.GetOverlayContainers(), Has.Count.EqualTo(1));
        Assert.That(page.GetOverlayData(page.GetOverlayContainers().Single()), Is.SameAs(note));
        InvokePrivateAsync(editor, "PerformRedoAsync");
        Assert.That(page.GetOverlayContainers(), Is.Empty);
        editor.CloseTransientUi("STA cleanup");
    }

    private static void AssertStickyButton(Button button, string automationId)
    {
        Assert.Multiple(() =>
        {
            Assert.That(button.MinHeight, Is.GreaterThanOrEqualTo(32));
            Assert.That(AutomationProperties.GetAutomationId(button), Is.EqualTo(automationId));
            Assert.That(AutomationProperties.GetName(button), Is.Not.Empty);
            Assert.That(AutomationProperties.GetHelpText(button), Is.Not.Empty);
        });
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        Assert.That(peer, Is.Not.Null, automationId);
        Assert.That(peer!.GetPattern(PatternInterface.Invoke), Is.Not.Null, automationId);
    }

    private static void AssertStickyButtonLocalization(Button button, string key, string automationId)
    {
        string label = LocalizationService.Get(key);
        Assert.Multiple(() =>
        {
            Assert.That(button.Content, Is.EqualTo(label), automationId);
            Assert.That(ToolTipService.GetToolTip(button), Is.EqualTo(label), automationId);
            Assert.That(AutomationProperties.GetName(button), Is.EqualTo(label), automationId);
            Assert.That(AutomationProperties.GetHelpText(button), Is.EqualTo(label), automationId);
        });
    }

    private static Application CreateTestApplication()
    {
        var application = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        if (application.Resources["SleekScrollViewer"] == null)
            application.Resources["SleekScrollViewer"] = new Style(typeof(ScrollViewer));
        if (application.Resources["CompactComboBox"] == null)
            application.Resources["CompactComboBox"] = new Style(typeof(ComboBox));
        if (application.Resources["ToolbarFocusVisualStyle"] == null)
            application.Resources["ToolbarFocusVisualStyle"] = new Style(typeof(Control));
        return application;
    }

    private static T GetPrivateField<T>(object target, string name)
    {
        return (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
            ?? throw new AssertionException($"Private field '{name}' was not initialized."));
    }

    private static void InvokePrivate(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Private method '{methodName}' was not found.");
        method.Invoke(target, arguments);
    }

    private static void InvokePrivateAsync(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Private method '{methodName}' was not found.");
        ((Task)method.Invoke(target, null)!).GetAwaiter().GetResult();
    }

    private static void EnsureWpfEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");
    }

    private static string ReadProjectFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }
}
