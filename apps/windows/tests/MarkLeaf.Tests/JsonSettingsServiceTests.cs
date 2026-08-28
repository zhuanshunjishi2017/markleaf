using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class JsonSettingsServiceTests
{
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
