using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class JsonSettingsServiceTests
{
    [TestMethod]
    [DataRow(SettingsSaveScope.Window)]
    [DataRow(SettingsSaveScope.ClearRecentHistory)]
    public async Task NonSharedSave_DoesNotMakeStaleSharedValuesLookLikeLocalEdits(SettingsSaveScope scope)
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        try
        {
            await new JsonSettingsService(file, new TestLogger()).SaveAsync(new AppSettings());
            var serviceA = new JsonSettingsService(file, new TestLogger());
            var serviceB = new JsonSettingsService(file, new TestLogger());
            var a = await serviceA.LoadAsync();
            var b = await serviceB.LoadAsync();
            a.Editor.SourceFontSize = 22;
            await serviceA.SaveAsync(a, SettingsSaveScope.Shared);
            await serviceB.SaveAsync(b, scope);
            b.File.AutoSaveEnabled = true;
            await serviceB.SaveAsync(b, SettingsSaveScope.Shared);
            Assert.AreEqual(22, (await serviceB.LoadAsync()).Editor.SourceFontSize);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task FailedSave_DoesNotAdvanceTheMergeBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            await service.SaveAsync(new AppSettings());
            var settings = await service.LoadAsync();
            settings.Editor.SourceFontSize = 22;
            File.Move(file, file + ".backup");
            Directory.CreateDirectory(file);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(settings, SettingsSaveScope.Shared));
            Directory.Delete(file);
            File.Move(file + ".backup", file);
            await service.SaveAsync(settings, SettingsSaveScope.Shared);
            var saved = await service.LoadAsync();
            Assert.AreEqual(22, saved.Editor.SourceFontSize);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task SharedSave_RefreshesBroadcastSourceWithCommittedPeerSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        try
        {
            await new JsonSettingsService(file, new TestLogger()).SaveAsync(new AppSettings());
            var serviceA = new JsonSettingsService(file, new TestLogger());
            var serviceB = new JsonSettingsService(file, new TestLogger());
            var a = await serviceA.LoadAsync();
            var b = await serviceB.LoadAsync();
            var shortcuts = b.Shortcut;
            var formatter = b.CodeFormatter;
            var window = b.MainWindow;
            var workspace = b.Workspace;
            a.Editor.SourceFontSize = 22;
            a.CodeFormatter.SqlDialect = "postgres";
            a.Shortcut.Overrides["ToggleBold"] = "Ctrl+Alt+B";
            await serviceA.SaveAsync(a, SettingsSaveScope.Shared);
            b.File.AutoSaveEnabled = true;
            b.Appearance.ZoomPercent = 75;
            await serviceB.SaveAsync(b, SettingsSaveScope.Shared);
            Assert.AreEqual(22, b.Editor.SourceFontSize);
            Assert.AreEqual("postgres", b.CodeFormatter.SqlDialect);
            Assert.AreEqual("Ctrl+Alt+B", b.Shortcut.Overrides["ToggleBold"]);
            Assert.AreEqual(75, b.Appearance.ZoomPercent);
            Assert.AreSame(shortcuts, b.Shortcut);
            Assert.AreSame(formatter, b.CodeFormatter);
            Assert.AreSame(window, b.MainWindow);
            Assert.AreSame(workspace, b.Workspace);
            MarkLeaf.App.SharedSettingsNotice.From(b).ApplyTo(a);
            Assert.AreEqual(22, a.Editor.SourceFontSize);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow("{}", false)]
    [DataRow("{\"editor\":{\"inlineCommonActions\":true}}", true)]
    [DataRow("{\"editor\":{\"inlineCommonActions\":false}}", false)]
    public async Task InlineCommonActions_SurvivesSaveAndReload(string json, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(file, json);
            var service = new JsonSettingsService(file, new TestLogger());
            await service.SaveAsync(await service.LoadAsync());
            var reloaded = await service.LoadAsync();
            await service.SaveAsync(reloaded);
            using var saved = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file));
            Assert.IsTrue(saved.RootElement.GetProperty("editor").TryGetProperty("inlineCommonActions", out var value),
                "Saving preferences must retain the context menu presentation choice.");
            Assert.AreEqual(expected, value.GetBoolean());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsWindowSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");

        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            var expected = new AppSettings
            {
                MainWindow = new WindowSettings
                {
                    Left = 42,
                    Top = 64,
                    Width = 1400,
                    Height = 900,
                    Dpi = 144,
                    IsMaximized = true,
                    WorkspaceWidth = 260,
                    OutlineWidth = 240,
                    OutlineDetached = true,
                },
                Workspace = new WorkspaceSettings
                {
                    LastFolder = @"D:\Notes",
                    RecentFolders = [@"D:\Notes", @"D:\Archive"],
                },
                Editor = new EditorSettings
                {
                    VisualCjkAutoSpacing = false,
                    ExitBlockOnEmptyEnter = true,
                    UseShiftEnterHardBreak = false,
                    MarkdownCodeFence = "tilde",
                    MarkdownEmphasisMarker = "underscore",
                    MarkdownBulletMarker = "plus",
                },
            };

            await service.SaveAsync(expected);
            var actual = await service.LoadAsync();

            Assert.AreEqual(42, actual.MainWindow.Left);
            Assert.AreEqual(144, actual.MainWindow.Dpi);
            Assert.AreEqual(260, actual.MainWindow.WorkspaceWidth);
            Assert.AreEqual(240, actual.MainWindow.OutlineWidth);
            Assert.IsTrue(actual.MainWindow.OutlineDetached);
            Assert.IsTrue(actual.MainWindow.IsMaximized);
            Assert.AreEqual(@"D:\Notes", actual.Workspace.LastFolder);
            CollectionAssert.AreEqual(new[] { @"D:\Notes", @"D:\Archive" }, actual.Workspace.RecentFolders);
            Assert.IsFalse(actual.Editor.VisualCjkAutoSpacing);
            Assert.IsTrue(actual.Editor.ExitBlockOnEmptyEnter);
            Assert.IsFalse(actual.Editor.UseShiftEnterHardBreak);
            Assert.AreEqual("tilde", actual.Editor.MarkdownCodeFence);
            Assert.AreEqual("underscore", actual.Editor.MarkdownEmphasisMarker);
            Assert.AreEqual("plus", actual.Editor.MarkdownBulletMarker);
            Assert.IsFalse(File.Exists(file + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public async Task WindowScopedSave_PreservesPrimaryWindowAndMergesRecentItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, """
            {
              "mainWindow": { "left": 111, "width": 900, "height": 700 },
              "workspace": {
                "openDocuments": [{ "path": "C:\\work\\primary.md" }],
                "activeDocumentIndex": 0,
                "recentFiles": ["C:\\work\\A.md"]
              },
              "editor": { "sourceFontSize": 18 }
            }
            """);

        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            var baseline = await service.LoadAsync();
            Assert.HasCount(1, baseline.Workspace.RecentFiles);

            // A peer wrote C after this isolated window loaded its baseline. The
            // local window omitted A while adding B, so A is a local deletion and
            // C is a peer addition that must survive.
            await File.WriteAllTextAsync(file, """
                {
                  "mainWindow": { "left": 222, "width": 1000, "height": 750 },
                  "workspace": {
                    "openDocuments": [{ "path": "C:\\work\\peer.md" }],
                    "activeDocumentIndex": 0,
                    "recentFiles": ["C:\\work\\A.md", "C:\\work\\C.md"]
                  },
                  "editor": { "sourceFontSize": 20 }
                }
                """);
            var incoming = new AppSettings
            {
                MainWindow = new WindowSettings
                {
                    Left = 999,
                    Width = baseline.MainWindow.Width,
                    Height = baseline.MainWindow.Height,
                    Dpi = baseline.MainWindow.Dpi,
                    WorkspaceWidth = baseline.MainWindow.WorkspaceWidth,
                    OutlineWidth = baseline.MainWindow.OutlineWidth,
                },
                Workspace = new WorkspaceSettings
                {
                    LastFolder = baseline.Workspace.LastFolder,
                    LastFile = baseline.Workspace.LastFile,
                    RecentFiles = ["C:\\work\\B.md"],
                },
                Editor = new EditorSettings
                {
                    SourceFontSize = 16,
                },
            };

            await service.SaveAsync(incoming, SettingsSaveScope.Window);
            var actual = await service.LoadAsync();

            Assert.AreEqual(222, actual.MainWindow.Left);
            Assert.AreEqual("C:\\work\\peer.md", actual.Workspace.OpenDocuments.Single().Path);
            Assert.AreEqual(20, actual.Editor.SourceFontSize);
            CollectionAssert.AreEqual(new[] { @"C:\work\B.md", @"C:\work\C.md" },
                actual.Workspace.RecentFiles);
            Assert.AreEqual(20, actual.Editor.SourceFontSize);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task SharedScopedSave_PreservesPrimaryWindowAndAppliesSharedPreferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, """
            {
              "mainWindow": { "left": 333, "width": 950, "height": 725 },
              "workspace": { "openDocuments": [{ "path": "C:\\work\\primary.md" }] },
              "editor": { "sourceFontSize": 18 }
            }
            """);

        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            var incoming = new AppSettings
            {
                MainWindow = new WindowSettings { Left = 888, Width = 1200, Height = 800 },
                Workspace = new WorkspaceSettings(),
                Editor = new EditorSettings { SourceFontSize = 22 },
            };

            await service.SaveAsync(incoming, SettingsSaveScope.Shared);
            var actual = await service.LoadAsync();

            Assert.AreEqual(333, actual.MainWindow.Left);
            Assert.AreEqual("C:\\work\\primary.md", actual.Workspace.OpenDocuments.Single().Path);
            Assert.AreEqual(22, actual.Editor.SourceFontSize);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task SharedScopedSave_DoesNotDeletePeerRecentItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, """
            {
              "workspace": { "recentFiles": ["C:\\work\\A.md"] }
            }
            """);

        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            var baseline = await service.LoadAsync();
            Assert.HasCount(1, baseline.Workspace.RecentFiles);

            await File.WriteAllTextAsync(file, """
                {
                  "workspace": { "recentFiles": ["C:\\work\\A.md", "C:\\work\\C.md"] }
                }
                """);
            var incoming = baseline;
            await service.SaveAsync(incoming, SettingsSaveScope.Shared);

            var actual = await service.LoadAsync();
            CollectionAssert.AreEqual(new[] { @"C:\work\A.md", @"C:\work\C.md" },
                actual.Workspace.RecentFiles);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ClearRecentHistoryScope_RemovesPeerAdditionsAndPreservesLatestState()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, """
            {
              "mainWindow": { "left": 555 },
              "workspace": { "recentFiles": ["C:\\work\\A.md"] },
              "editor": { "sourceFontSize": 18 }
            }
            """);

        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            await service.LoadAsync();
            await File.WriteAllTextAsync(file, """
                {
                  "mainWindow": { "left": 666 },
                  "workspace": { "recentFiles": ["C:\\work\\A.md", "C:\\work\\C.md"] },
                  "editor": { "sourceFontSize": 21 }
                }
                """);

            await service.SaveAsync(new AppSettings(), SettingsSaveScope.ClearRecentHistory);
            var actual = await service.LoadAsync();

            Assert.IsEmpty(actual.Workspace.RecentFiles);
            Assert.IsEmpty(actual.Workspace.RecentFolders);
            Assert.AreEqual(666, actual.MainWindow.Left);
            Assert.AreEqual(21, actual.Editor.SourceFontSize);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Save_DoesNotOverwriteUnreadableSettingsWithDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        const string invalidJson = "{ not-json";
        await File.WriteAllTextAsync(file, invalidJson);

        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            var incoming = new AppSettings { Editor = new EditorSettings { SourceFontSize = 30 } };

            await Assert.ThrowsExactlyAsync<System.Text.Json.JsonException>(() =>
                service.SaveAsync(incoming, SettingsSaveScope.Window));
            Assert.AreEqual(invalidJson, await File.ReadAllTextAsync(file));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task FullSave_MergesPeerSharedPreferencesWithoutRollback()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, """
            {
              "editor": { "sourceFontSize": 14 },
              "appearance": { "showCodeHighlight": true }
            }
            """);

        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            var baseline = await service.LoadAsync();
            Assert.AreEqual(14, baseline.Editor.SourceFontSize);

            await File.WriteAllTextAsync(file, """
                {
                  "editor": { "sourceFontSize": 20 },
                  "appearance": { "showCodeHighlight": true }
                }
                """);
            var incoming = new AppSettings
            {
                Editor = baseline.Editor,
                Appearance = new AppearanceSettings { ShowCodeHighlight = false },
            };

            await service.SaveAsync(incoming, SettingsSaveScope.Full);
            var actual = await service.LoadAsync();

            Assert.AreEqual(20, actual.Editor.SourceFontSize);
            Assert.IsFalse(actual.Appearance.ShowCodeHighlight);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ConcurrentWindowScopedSavesPreserveBothRecentAdditions()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        try
        {
            var service = new JsonSettingsService(file, new TestLogger());
            var first = new AppSettings();
            first.Workspace.RecentFiles = ["C:\\work\\first.md"];
            await service.SaveAsync(first);

            var left = await service.LoadAsync();
            var right = await service.LoadAsync();
            var leftService = new JsonSettingsService(file, new TestLogger());
            var rightService = new JsonSettingsService(file, new TestLogger());
            leftService.LoadAsync().GetAwaiter().GetResult();
            rightService.LoadAsync().GetAwaiter().GetResult();
            left.Workspace.RecentFiles = ["C:\\work\\left-new.md"];
            right.Workspace.RecentFiles = ["C:\\work\\right-new.md"];

            await Task.WhenAll(
                leftService.SaveAsync(left, SettingsSaveScope.Window),
                rightService.SaveAsync(right, SettingsSaveScope.Window));

            var actual = await service.LoadAsync();
            Assert.Contains(@"C:\work\left-new.md", actual.Workspace.RecentFiles);
            Assert.Contains(@"C:\work\right-new.md", actual.Workspace.RecentFiles);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Load_InvalidMarkdownSyntaxPreferences_UsesDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, """
            {
              "editor": {
                "markdownCodeFence": "invalid",
                "markdownEmphasisMarker": "invalid",
                "markdownBulletMarker": "invalid"
              }
            }
            """);

        try
        {
            var settings = await new JsonSettingsService(file, new TestLogger()).LoadAsync();

            Assert.AreEqual("backtick", settings.Editor.MarkdownCodeFence);
            Assert.AreEqual("asterisk", settings.Editor.MarkdownEmphasisMarker);
            Assert.AreEqual("dash", settings.Editor.MarkdownBulletMarker);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task Load_Version2Settings_AddsWorkspaceDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, """
            {
              "schemaVersion": 2,
              "mainWindow": { "width": 1280, "height": 800, "dpi": 96 }
            }
            """);

        try
        {
            var settings = await new JsonSettingsService(file, new TestLogger()).LoadAsync();

            Assert.AreEqual(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.IsNotNull(settings.Workspace);
            Assert.IsEmpty(settings.Workspace.RecentFolders);
            Assert.IsTrue(settings.Editor.VisualCjkAutoSpacing);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task Load_InvalidJson_ReturnsDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, "not-json");

        try
        {
            var logger = new TestLogger();
            var service = new JsonSettingsService(file, logger);

            var settings = await service.LoadAsync();

            Assert.AreEqual(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.IsNotEmpty(logger.Messages);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task Load_Version1Settings_MigratesPhysicalSizesToLogicalUnits()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(file, """
            {
              "schemaVersion": 1,
              "mainWindow": {
                "left": 309,
                "top": 152,
                "width": 1868,
                "height": 1353,
                "dpi": 168,
                "isMaximized": false,
                "workspaceWidth": 385,
                "outlineWidth": 385
              }
            }
            """);

        try
        {
            var service = new JsonSettingsService(file, new TestLogger());

            var settings = await service.LoadAsync();

            Assert.AreEqual(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.AreEqual(309, settings.MainWindow.Left);
            Assert.AreEqual(1067, settings.MainWindow.Width);
            Assert.AreEqual(773, settings.MainWindow.Height);
            Assert.AreEqual(220, settings.MainWindow.WorkspaceWidth);
            Assert.AreEqual(220, settings.MainWindow.OutlineWidth);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
