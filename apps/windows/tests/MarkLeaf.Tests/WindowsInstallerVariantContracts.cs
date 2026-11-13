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

    [TestMethod]
    public void SlimRuntimeCheckDoesNotExpandAppBeforeTheWizardInitializesIt()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Setup", "markleaf.iss"));
        var code = script[(script.IndexOf("[Code]", StringComparison.Ordinal)..)];
        var initializeSetupStart = code.IndexOf("function InitializeSetup", StringComparison.Ordinal);
        var prepareToInstallStart = code.IndexOf("function PrepareToInstall", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, initializeSetupStart);
        Assert.IsGreaterThanOrEqualTo(0, prepareToInstallStart);

        var initializeSetup = code[initializeSetupStart..prepareToInstallStart];
        var prepareToInstall = code[prepareToInstallStart..];
        Assert.DoesNotContain("ExpandConstant('{app}", initializeSetup);
        Assert.Contains("WizardDirValue", prepareToInstall);
    }
}
