using System.Text;

namespace MarkLeaf.Services.Logging;

public sealed class FileLogger : IAppLogger, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly StreamWriter _writer;

    public FileLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(
            logDirectory,
            $"markleaf-{DateTime.Now:yyyyMMdd}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
            new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _writer.Dispose();
        }
    }

    private void Write(string level, string message)
    {
        lock (_syncRoot)
        {
            _writer.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
        }
    }
}
