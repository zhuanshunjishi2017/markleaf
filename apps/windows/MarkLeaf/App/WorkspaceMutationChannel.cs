using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MarkLeaf.App;

internal sealed record WorkspaceMutationNotice(string Source, string? Destination, bool IsDirectory);

// Each Windows window is a process. Notify peer windows only after a successful
// disk mutation; the messages never authorize a filesystem operation in a peer.
internal sealed class WorkspaceMutationChannel : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _identity;
    private readonly Action<WorkspaceMutationNotice> _receive;
    private readonly Task _listener;
    private static readonly string Scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        (Environment.ProcessPath ?? "MarkLeaf") + ":" + Process.GetCurrentProcess().SessionId)))[..16];
    internal static string CurrentIdentity => Scope + "." + Environment.ProcessId;

    internal WorkspaceMutationChannel(string identity, Action<WorkspaceMutationNotice> receive)
    {
        _identity = identity;
        _receive = receive;
        _listener = ListenAsync();
    }

    internal static async Task BroadcastAsync(WorkspaceMutationNotice notice)
    {
        using var current = Process.GetCurrentProcess();
        var peers = Process.GetProcessesByName(current.ProcessName);
        try
        {
            await Task.WhenAll(peers.Where(p => p.Id != Environment.ProcessId)
                .Select(p => SendAsync(Scope + "." + p.Id, notice)));
        }
        finally { foreach (var peer in peers) peer.Dispose(); }
    }

    internal static async Task<bool> SendAsync(string identity, WorkspaceMutationNotice notice)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "MarkLeaf.Workspace." + identity,
                PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(300);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(notice));
            return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        { return false; }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream("MarkLeaf.Workspace." + _identity,
                    PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var json = await reader.ReadLineAsync(_stop.Token);
                if (json is null || json.Length > 65536) continue;
                var notice = JsonSerializer.Deserialize<WorkspaceMutationNotice>(json);
                if (notice is not null && Path.IsPathFullyQualified(notice.Source)
                    && (notice.Destination is null || Path.IsPathFullyQualified(notice.Destination)))
                    _receive(notice);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        // Do not block the UI thread while shutting down a pipe listener.
        _ = _listener.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
