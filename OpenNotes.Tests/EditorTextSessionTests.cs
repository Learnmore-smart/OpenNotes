using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;
using PdfSharpCore.Pdf;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class EditorTextSessionTests
{
    [Test]
    public void ProductionTextSessionCommitsBeforeSaveAndWritesLatestContentOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string dataRoot = Path.Combine(root, "data");
        string pdfPath = Path.Combine(root, "text-session.pdf");
        CreateTestPdf(pdfPath);

        string? previousDataRoot = Environment.GetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable);
        string? previousWindir = Environment.GetEnvironmentVariable("WINDIR");
        Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, dataRoot);
        if (string.IsNullOrWhiteSpace(previousWindir))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot"));
        try
        {
            RunOnSta(async () =>
            {
                var app = CreateTestApplication();
                var editor = new EditorPage();
                SetPrivateField(editor, "_currentPdfPath", pdfPath);

                var page = new PdfPageControl();
                SetPrivateListField(editor, "_pageControls", page);

                var createTextBox = typeof(EditorPage).GetMethod(
                    "CreateTextBox",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var container = (Grid)createTextBox.Invoke(
                    editor,
                    new object?[]
                    {
                        page,
                        new Point(24, 24),
                        null,
                        null,
                        "before",
                        false,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null
                    })!;
                var textBox = container.Children.OfType<TextBox>().Single();

                var beginSession = typeof(EditorPage).GetMethod(
                    "BeginTextEditSession",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                beginSession.Invoke(editor, new object[] { textBox });
                textBox.Text = "latest text";

                var save = typeof(EditorPage).GetMethod(
                    "SaveCurrentDocumentAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var saveTask = (Task<bool>)save.Invoke(editor, null)!;
                Assert.That(await saveTask, Is.True);

                var versions = VersionControlService.GetVersions(pdfPath);
                Assert.That(versions, Has.Count.EqualTo(1));

                await using (var reopened = new PdfService())
                {
                    await reopened.LoadPdfAsync(pdfPath);
                    Assert.That(
                        reopened.ExtractedAnnotations[0].Texts.Single().Text,
                        Is.EqualTo("latest text"));
                }

                Assert.That(await editor.ReleaseResourcesAsync(), Is.True);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ProductInfo.DataRootOverrideEnvironmentVariable,
                previousDataRoot);
            Environment.SetEnvironmentVariable("WINDIR", previousWindir);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public void ProductionCloseKeepsLateTextMutationInTheFinalSnapshot()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string dataRoot = Path.Combine(root, "data");
        string pdfPath = Path.Combine(root, "close-late-text.pdf");
        CreateTestPdf(pdfPath);

        string? previousDataRoot = Environment.GetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable);
        string? previousWindir = Environment.GetEnvironmentVariable("WINDIR");
        Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, dataRoot);
        if (string.IsNullOrWhiteSpace(previousWindir))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot"));
        try
        {
            RunOnSta(async () =>
            {
                var app = CreateTestApplication();
                var editor = CreateEditorWithTextBox(pdfPath, "before-close", out var textBox);
                var saveCoordinator = GetPrivateField<DocumentSaveCoordinator>(editor, "_documentSaveCoordinator");

                var releasePath = NewSignal();
                Task heldPath = PdfSaveCoordinator.RunExclusiveAsync(pdfPath, () => releasePath.Task);
                Task<bool> close = editor.PrepareForCloseAsync();

                await WaitForConditionAsync(
                    () => !editor.IsEnabled && GetPrivateField<Task>(saveCoordinator, "_inFlight") != null);

                // The live WPF model changes before its TextChanged callback
                // reaches MarkDirty, matching the late-ink/text ordering that
                // close must retain while the PDF path lease is occupied.
                textBox.Text = "late-close-text";
                releasePath.TrySetResult(true);

                Assert.That(await close.WaitAsync(TimeSpan.FromSeconds(10)), Is.True);
                await heldPath;

                await using (var reopened = new PdfService())
                {
                    await reopened.LoadPdfAsync(pdfPath);
                    Assert.That(
                        reopened.ExtractedAnnotations[0].Texts.Single().Text,
                        Is.EqualTo("late-close-text"));
                }

                Assert.That(await editor.ReleaseResourcesAsync(), Is.True);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ProductInfo.DataRootOverrideEnvironmentVariable,
                previousDataRoot);
            Environment.SetEnvironmentVariable("WINDIR", previousWindir);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public void ProductionNavigationReopensCoordinatorForEditsAfterReturning()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string dataRoot = Path.Combine(root, "data");
        string pdfPath = Path.Combine(root, "navigation-late-text.pdf");
        CreateTestPdf(pdfPath);

        string? previousDataRoot = Environment.GetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable);
        string? previousWindir = Environment.GetEnvironmentVariable("WINDIR");
        Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, dataRoot);
        if (string.IsNullOrWhiteSpace(previousWindir))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot"));
        try
        {
            RunOnSta(async () =>
            {
                var app = CreateTestApplication();
                var editor = CreateEditorWithTextBox(pdfPath, "before-navigation", out var textBox);
                var saveCoordinator = GetPrivateField<DocumentSaveCoordinator>(editor, "_documentSaveCoordinator");

                var releasePath = NewSignal();
                Task heldPath = PdfSaveCoordinator.RunExclusiveAsync(pdfPath, () => releasePath.Task);
                Task<bool> navigation = editor.PrepareForNavigationAsync();
                await WaitForConditionAsync(
                    () => !editor.IsEnabled && GetPrivateField<Task>(saveCoordinator, "_inFlight") != null);

                textBox.Text = "during-navigation";
                releasePath.TrySetResult(true);
                Assert.That(await navigation.WaitAsync(TimeSpan.FromSeconds(10)), Is.True);
                await heldPath;

                // Frame_Navigated/ActivateTab calls this production method
                // when the editor returns from the journal. The coordinator
                // must be reopened together with the input admission.
                editor.ResumeDocumentInteraction();
                textBox.Text = "after-navigation";
                Assert.That(await editor.AutoSaveAsync(), Is.True);

                await using (var reopened = new PdfService())
                {
                    await reopened.LoadPdfAsync(pdfPath);
                    Assert.That(
                        reopened.ExtractedAnnotations[0].Texts.Single().Text,
                        Is.EqualTo("after-navigation"));
                }

                Assert.That(await editor.ReleaseResourcesAsync(), Is.True);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ProductInfo.DataRootOverrideEnvironmentVariable,
                previousDataRoot);
            Environment.SetEnvironmentVariable("WINDIR", previousWindir);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void CreateTestPdf(string filePath)
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.Save(filePath);
    }

    private static Application CreateTestApplication()
    {
        if (Application.Current != null)
            return Application.Current;

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        app.Resources["SleekScrollViewer"] = new Style(typeof(ScrollViewer));
        app.Resources["CompactComboBox"] = new Style(typeof(ComboBox));
        app.Resources["ToolbarFocusVisualStyle"] = new Style(typeof(Control));
        return app;
    }

    private static EditorPage CreateEditorWithTextBox(
        string pdfPath,
        string initialText,
        out TextBox textBox)
    {
        var editor = new EditorPage();
        SetPrivateField(editor, "_currentPdfPath", pdfPath);

        var page = new PdfPageControl();
        SetPrivateListField(editor, "_pageControls", page);

        var createTextBox = typeof(EditorPage).GetMethod(
            "CreateTextBox",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var container = (Grid)createTextBox.Invoke(
            editor,
            new object?[]
            {
                page,
                new Point(24, 24),
                null,
                null,
                initialText,
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null
            })!;
        textBox = container.Children.OfType<TextBox>().Single();

        var beginSession = typeof(EditorPage).GetMethod(
            "BeginTextEditSession",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        beginSession.Invoke(editor, new object[] { textBox });
        textBox.Text = "initial-edit";
        return editor;
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200; i++)
        {
            if (condition())
                return;

            await System.Windows.Threading.Dispatcher.Yield(
                System.Windows.Threading.DispatcherPriority.Background);
        }

        Assert.Fail("The production editor did not reach the expected lifecycle barrier.");
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (T)field.GetValue(target)!;
    }

    private static void SetPrivateListField(EditorPage editor, string name, PdfPageControl page)
    {
        var field = typeof(EditorPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pages = (System.Collections.IList)field.GetValue(editor)!;
        pages.Add(page);
    }

    private static void RunOnSta(Func<Task> body)
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                Task task;
                try
                {
                    task = body();
                }
                catch (Exception ex)
                {
                    failure = ex;
                    dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Send);
                    return;
                }

                task.ContinueWith(completedTask =>
                {
                    if (completedTask.IsFaulted)
                        failure = completedTask.Exception?.GetBaseException();
                    else if (completedTask.IsCanceled)
                        failure = new TaskCanceledException(completedTask);
                    dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Send);
                }, TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completed.Wait(TimeSpan.FromSeconds(30));
        Assert.That(completed.IsSet, Is.True, "STA editor test did not complete within 30 seconds.");
        thread.Join(TimeSpan.FromSeconds(5));
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
