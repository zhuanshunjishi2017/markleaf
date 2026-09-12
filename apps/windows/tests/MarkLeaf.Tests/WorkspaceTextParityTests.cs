using System.Text.Json;
using MarkLeaf.Workspace;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class WorkspaceTextParityTests
{
    private static JsonDocument ReadFixture()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "workspace-text.json")));

    [TestMethod]
    public async Task TreeListAndSearch_UseTheSameTextFileScope()
    {
        using var fixture = ReadFixture();
        var files = fixture.RootElement.GetProperty("files").EnumerateArray().ToArray();
        var expected = files.Where(item => item.GetProperty("included").GetBoolean())
            .Select(item => item.GetProperty("name").GetString()!).ToArray();
        var root = Path.Combine(Path.GetTempPath(), "markleaf-text-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var item in files)
                await File.WriteAllTextAsync(Path.Combine(root, item.GetProperty("name").GetString()!), "needle");
            var service = new WorkspaceService();
            CollectionAssert.AreEquivalent(expected, (await service.GetChildrenAsync(root)).Select(item => item.Name).ToArray());
            CollectionAssert.AreEquivalent(expected, (await service.GetDocumentsAsync(root)).Select(item => item.Name).ToArray());
            CollectionAssert.AreEquivalent(expected, (await service.SearchAsync(root, "needle")).Select(item => item.FileName).ToArray());
        }
        finally { Directory.Delete(root, true); }
    }
}
