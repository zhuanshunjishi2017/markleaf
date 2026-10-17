using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.App;

internal sealed record SharedSettingsNotice(AppSettings Shared)
{
    public static SharedSettingsNotice From(AppSettings settings) => new(CopySharedSource(settings));

    public void ApplySharedTo(AppSettings settings)
    {
        settings.File = Shared.File;
        settings.Editor = Shared.Editor;
        settings.Appearance = Shared.Appearance;
        settings.General = Shared.General;
        settings.Image = Shared.Image;
        settings.Export = Shared.Export;
        settings.Shortcut = Shared.Shortcut;
        settings.CodeFormatter = Shared.CodeFormatter;
        settings.MarkdownStyle = Shared.MarkdownStyle;
        settings.ColorTheme = Shared.ColorTheme;
    }

    private static AppSettings CopySharedSource(AppSettings source)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, source, options);
        stream.Position = 0;
        return JsonSerializer.Deserialize<AppSettings>(stream, options) ?? new AppSettings();
    }
}

// Every Windows window owns a process. A full settings object is deliberately
// not broadcast: this carries only shared appearance ownership. Window state
// remains local to each process.
internal sealed class SettingsChangeChannel : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _identity;
    private readonly Action<SharedSettingsNotice> _receive;
    private readonly Task _listener;
    private static readonly string Scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        (Environment.ProcessPath ?? "MarkLeaf") + ":" + Process.GetCurrentProcess().SessionId)))[..16];
    internal static string CurrentIdentity => Scope + "." + Environment.ProcessId;

    internal SettingsChangeChannel(string identity, Action<SharedSettingsNotice> receive)
    {
        _identity = identity;
        _receive = receive;
        _listener = ListenAsync();
    }

    internal static async Task BroadcastAsync(SharedSettingsNotice notice)
    {
        using var current = Process.GetCurrentProcess();
        var peers = Process.GetProcessesByName(current.ProcessName);
        try
        {
            await Task.WhenAll(peers.Where(process => process.Id != Environment.ProcessId)
                .Select(process => SendAsync(Scope + "." + process.Id, notice)));
        }
        finally { foreach (var peer in peers) peer.Dispose(); }
    }

    private static async Task SendAsync(string identity, SharedSettingsNotice notice)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "MarkLeaf.Settings." + identity,
                PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(300);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(notice));
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream("MarkLeaf.Settings." + _identity,
                    PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var json = await reader.ReadLineAsync(_stop.Token);
                if (json is null || json.Length > 65536) continue;
                var notice = JsonSerializer.Deserialize<SharedSettingsNotice>(json);
                if (notice is not null) _receive(notice);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _ = _listener.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
