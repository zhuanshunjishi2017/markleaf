using MarkLeaf.Services.Logging;

namespace MarkLeaf.Tests;

internal sealed class TestLogger : IAppLogger
{
    public List<string> Messages { get; } = [];

    public void Info(string message) => Messages.Add(message);

    public void Warning(string message) => Messages.Add(message);

    public void Error(string message, Exception? exception = null) => Messages.Add(message);
}

