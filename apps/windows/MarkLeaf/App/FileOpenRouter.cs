using System.IO.Pipes;
using System.Text;

namespace MarkLeaf.App;

internal sealed class FileOpenRouter : IDisposable
{
    private const string NamePrefix = "MarkLeaf.FileOpen.";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Func<string, Task> _openFile;
    private Task? _listenerTask;

    private FileOpenRouter(Mutex mutex, Func<string, Task> openFile)
    {
        _mutex = mutex;
        _openFile = openFile;
    }

    public static bool TryStartPrimary(Func<string, Task> openFile, out FileOpenRouter? router)
    {
        var mutex = new Mutex(true, GetName(), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            router = null;
            return false;
        }

        router = new FileOpenRouter(mutex, openFile);
        router._listenerTask = router.ListenAsync();
        return true;
    }

    public static async Task<bool> TryForwardAsync(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", GetName(), PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(750);
            using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(Path.GetFullPath(path));
            return true;
        }
        catch (IOException) { return false; }
        catch (TimeoutException) { return false; }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _listenerTask?.Wait(500); } catch (AggregateException) { }
        _cancellation.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    GetName(), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cancellation.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var path = (await reader.ReadLineAsync())?.Trim();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    await _openFile(path);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { return; }
            catch (IOException) { }
        }
    }

    private static string GetName() => NamePrefix + Environment.UserName;
}
