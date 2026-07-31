using MarkLeaf.App;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class LaunchOptionsTests
{
    [TestMethod]
    public void Parse_ReadsStage2SmokeCommandOptions()
    {
        var report = Path.GetFullPath("command-report.json");

        var options = LaunchOptions.Parse(
        [
            "--smoke-command", "ToggleFocusMode",
            "--command-report", report,
            "--editor-smoke-report", report,
            "--editor-web-root", ".\\editor-root",
            "--editor-state-report", report,
            "--editor-command-smoke", "setHeading1",
            "--editor-command-report", report,
            "--document-smoke-input", report,
            "--document-smoke-output", report,
            "--document-smoke-report", report,
        ]);

        Assert.AreEqual("ToggleFocusMode", options.SmokeCommand);
        Assert.AreEqual(report, options.CommandReportPath);
        Assert.AreEqual(report, options.EditorSmokeReportPath);
        Assert.AreEqual(Path.GetFullPath(".\\editor-root"), options.EditorWebRoot);
        Assert.AreEqual(report, options.EditorStateReportPath);
        Assert.AreEqual("setHeading1", options.EditorCommandSmoke);
        Assert.AreEqual(report, options.EditorCommandReportPath);
        Assert.AreEqual(report, options.DocumentSmokeInputPath);
        Assert.AreEqual(report, options.DocumentSmokeOutputPath);
        Assert.AreEqual(report, options.DocumentSmokeReportPath);
    }
}
