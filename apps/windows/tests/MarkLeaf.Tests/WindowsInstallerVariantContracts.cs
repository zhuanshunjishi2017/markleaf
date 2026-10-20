namespace MarkLeaf.Tests;

[TestClass]
public sealed class WindowsInstallerVariantContracts
{
    [TestMethod]
    public void SlimInstallersCannotOverwriteASelfContainedInstallation()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Setup", "markleaf.iss"));

        Assert.Contains("RejectSlimOverSelfContained", script);
        Assert.Contains("MarkLeaf.self-contained", script);
        Assert.Contains("Type: files; Name: \"{app}\\MarkLeaf.self-contained\"", script);
    }
}
