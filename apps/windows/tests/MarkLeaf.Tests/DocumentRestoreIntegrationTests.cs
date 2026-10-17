using System.Reflection;
using System.Text.Json;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Native.ModernMenus;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI;
using MarkLeaf.UI.Controls;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class DocumentRestoreIntegrationTests
{
    [STATestMethod]
    public void ReopeningSendsSelectionsAndViewportToTheEditor()
    {
        WithForm(form =>
        {
            var session = new EditorSession();
            session.TransitionTo(EditorLifecycleState.Initializing);
            session.TransitionTo(EditorLifecycleState.LoadingPage);
            session.TransitionTo(EditorLifecycleState.WaitingForEditorReady);
            session.TransitionTo(EditorLifecycleState.Ready);
            var messages = new List<string>();
            using var web = new WebView2();
            using var loading = new EditorLoadingView();
            var logger = (IAppLogger)Field("_logger").GetValue(form)!;
            var paths = (ApplicationPaths)Field("_paths").GetValue(form)!;
            var host = new EditorHostController(web, loading, session, logger,
                AppContext.BaseDirectory, paths.WebView2UserDataDirectory, () => {}, messages.Add);
            ((EditorHostController?)Field("_editorHost").GetValue(form))?.Dispose();
            Field("_editorHost").SetValue(form, host);
            var history = (List<MarkdownDocument>)Field("_closedTabHistory").GetValue(form)!;
            history.Add(new MarkdownDocument { Markdown = "closed buffer", IsDirty = true,
                VisualSelectionFrom = 1, VisualSelectionTo = 4, SourceSelectionFrom = 2, SourceSelectionTo = 5,
                ScrollTop = 42, SourceMode = true, SavedMarkdown = "saved buffer" });
            ((Task)Method("ReopenLastClosedTabAsync").Invoke(form, null)!).GetAwaiter().GetResult();
            var payload = messages.Select(message => JsonDocument.Parse(message).RootElement.Clone())
                .Last(message => message.GetProperty("type").GetString() == "loadDocument").GetProperty("payload");
            Assert.IsTrue(payload.GetProperty("restoreViewState").GetBoolean());
            Assert.AreEqual(42, payload.GetProperty("scrollTop").GetDouble());
            Assert.AreEqual(1, payload.GetProperty("visualSelection").GetProperty("from").GetInt32());
            Assert.AreEqual(5, payload.GetProperty("sourceSelection").GetProperty("to").GetInt32());
            Assert.IsTrue(payload.GetProperty("sourceMode").GetBoolean());
            Assert.AreEqual("saved buffer", payload.GetProperty("savedMarkdown").GetString());
        });
    }
    [STATestMethod]
    public void ReopeningRetainsUserReadOnlyAndBothSelectionRanges()
    {
        WithForm(form =>
        {
            var record = new MarkdownDocument { Markdown = "closed buffer", IsDirty = true,
                IsUserReadOnly = true, HasUnsavedMetadataChanges = true,
                VisualSelectionFrom = 1, VisualSelectionTo = 4, SourceSelectionFrom = 2, SourceSelectionTo = 5,
                SavedMarkdown = "saved buffer", SourceMode = true };
            var history = (List<MarkdownDocument>)Field("_closedTabHistory").GetValue(form)!;
            history.Add(record);
            ((Task)Method("ReopenLastClosedTabAsync").Invoke(form, null)!).GetAwaiter().GetResult();
            var restored = (MarkdownDocument)Field("_document").GetValue(form)!;
            Assert.IsTrue(restored.IsUserReadOnly);
            Assert.IsTrue(restored.HasUnsavedMetadataChanges);
            Assert.AreEqual(1, restored.VisualSelectionFrom);
            Assert.AreEqual(4, restored.VisualSelectionTo);
            Assert.AreEqual(2, restored.SourceSelectionFrom);
            Assert.AreEqual(5, restored.SourceSelectionTo);
            Assert.AreEqual("closed buffer", restored.Markdown);
            Assert.AreEqual("saved buffer", restored.SavedMarkdown);
            Assert.IsTrue(restored.SourceMode);
            Assert.AreNotEqual(record.Id, restored.Id);
        });
    }

    [STATestMethod]
    public void EmptyUntitledTabCanBeSavedFromItsContextMenu()
    {
        WithForm(form =>
        {
            var document = new MarkdownDocument();
            var model = (EditorMenuModel)Method("BuildDocumentTabMenu").Invoke(form, [document, 0])!;
            Assert.IsTrue(model.Secondary.Single(entry => entry.Command == AppCommand.SaveDocument).State.IsEnabled);
        });
    }

    [STATestMethod]
    public void ReopeningPreservesTheClosedDocumentDiskBaseline()
    {
        WithForm(form =>
        {
            var fingerprint = new FileFingerprint(12, DateTimeOffset.UtcNow, "a".PadLeft(64, 'a'));
            var testDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            var record = new MarkdownDocument
            {
                FilePath = Path.Combine(testDirectory, "closed.md"),
                Markdown = "closed buffer",
                IsDirty = true,
                LastKnownFingerprint = fingerprint,
                LastKnownWriteTime = fingerprint.LastWriteTime,
            };
            var history = (List<MarkdownDocument>)Field("_closedTabHistory").GetValue(form)!;
            history.Add(record);
            ((Task)Method("ReopenLastClosedTabAsync").Invoke(form, null)!).GetAwaiter().GetResult();
            var restored = (MarkdownDocument)Field("_document").GetValue(form)!;
            Assert.AreEqual(fingerprint, restored.LastKnownFingerprint);
            Assert.AreEqual(fingerprint.LastWriteTime, restored.LastKnownWriteTime);
            Directory.Delete(testDirectory, recursive: true);
        });
    }

    [STATestMethod]
    public void WelcomeOpensAsReadOnly()
    {
        WithForm(form =>
        {
            ((Task)Method("ShowWelcomeAsync").Invoke(form, null)!).GetAwaiter().GetResult();
            var document = (MarkdownDocument)Field("_document").GetValue(form)!;
            Assert.IsTrue(document.IsReadOnly);
        });
    }

    [STATestMethod]
    public void BundledSampleOpensAsEditableUntitledCopy()
    {
        WithForm(form =>
        {
            ((Task)Method("OpenBundledSampleAsync").Invoke(form, ["alert-examples.md"])!).GetAwaiter().GetResult();
            var document = (MarkdownDocument)Field("_document").GetValue(form)!;
            Assert.IsNull(document.FilePath);
            Assert.IsFalse(document.IsReadOnly || document.IsUserReadOnly);
            Assert.IsFalse(document.IsDirty);
            Assert.IsTrue(document.Markdown.Contains("提示框", StringComparison.Ordinal));
        });
    }

    private static FieldInfo Field(string name) => typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static MethodInfo Method(string name) => typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void WithForm(Action<MainForm> test)
    {
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = ApplicationPaths.Create(directory);
            using var logger = new FileLogger(paths.LogDirectory);
            using var form = new MainForm(LaunchOptions.Parse([]), paths, new AppSettings(), new JsonSettingsService(paths.SettingsFile, logger), logger);
            test(form);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task WithFormAsync(Func<MainForm, Task> test)
    {
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = ApplicationPaths.Create(directory);
            using var logger = new FileLogger(paths.LogDirectory);
            using var form = new MainForm(LaunchOptions.Parse([]), paths, new AppSettings(), new JsonSettingsService(paths.SettingsFile, logger), logger);
            await test(form);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task WaitForDocumentAsync(MainForm form)
    {
        for (var attempt = 0; attempt < 40 && Field("_document").GetValue(form) is null; attempt++)
        {
            Application.DoEvents();
            await Task.Delay(25);
        }
        Application.DoEvents();
        await Task.Delay(25);
    }
}
