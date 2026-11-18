namespace MarkLeaf.Tests;

[TestClass]
public sealed class WindowsInstallerVariantContracts
{
    [TestMethod]
    public void SlimInstallersCannotOverwriteASelfContainedInstallation()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Setup", "markleaf.iss"));

        var code = script[script.IndexOf("[Code]", StringComparison.Ordinal)..];
        var prepare = System.Text.RegularExpressions.Regex.Match(code,
            @"#if SelfContained == ""0""\s+(?:(?!#endif)[\s\S])*?function PrepareToInstall\([\s\S]*?\bend;");
        Assert.IsTrue(prepare.Success, "Slim setup must check the selected /DIR before copying files.");
        Assert.Contains(@"{app}\hostfxr.dll", prepare.Value);
        Assert.Contains(@"{app}\MarkLeaf.self-contained", prepare.Value);
        Assert.Contains("Result := CustomMessage(", prepare.Value);
        Assert.Contains("MarkLeaf.self-contained", script);
        Assert.Contains("Type: files; Name: \"{app}\\MarkLeaf.self-contained\"", script);
    }
}
