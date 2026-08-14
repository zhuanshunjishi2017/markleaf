using MarkLeaf.Services.Logging;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class FileLoggerTests
{
    [TestMethod]
    public void MultipleLoggers_CanWriteWithoutExclusiveFileLock()
    {
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var first = new FileLogger(directory))
            using (var second = new FileLogger(directory))
            {
                first.Info("first");
                second.Info("second");
            }

            var text = string.Join(Environment.NewLine, Directory.GetFiles(directory).Select(File.ReadAllText));
            StringAssert.Contains(text, "first");
            StringAssert.Contains(text, "second");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
