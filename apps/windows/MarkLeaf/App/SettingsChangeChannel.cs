using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.App;

internal sealed record AppearanceSettingsNotice(
    string MarkdownStyle,
    string ColorTheme,
    bool FollowSystemColorMode,
    bool ShowCodeHighlight,
    string DefaultLightThemeId,
    string DefaultDarkThemeId)
{
    public static AppearanceSettingsNotice From(AppSettings settings) => new(
        settings.MarkdownStyle,
        settings.ColorTheme,
        settings.Appearance.FollowSystemColorMode,
        settings.Appearance.ShowCodeHighlight,
        settings.Appearance.DefaultLightThemeId,
        settings.Appearance.DefaultDarkThemeId);

    public void ApplyTo(AppSettings settings)
    {
        settings.MarkdownStyle = MarkdownStyle;
        settings.ColorTheme = ColorTheme;
        settings.Appearance.FollowSystemColorMode = FollowSystemColorMode;
        settings.Appearance.ShowCodeHighlight = ShowCodeHighlight;
        settings.Appearance.DefaultLightThemeId = DefaultLightThemeId;
        settings.Appearance.DefaultDarkThemeId = DefaultDarkThemeId;
    }
}

// Every Windows window owns a process. A full settings object is deliberately
// not broadcast: this carries only shared appearance ownership. Window state
// remains local to each process.
internal sealed class SettingsChangeChannel : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _identity;
    private readonly Action<AppearanceSettingsNotice> _receive;
    private readonly Task _listener;
    private static readonly string Scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        (Environment.ProcessPath ?? "MarkLeaf") + ":" + Process.GetCurrentProcess().SessionId)))[..16];
    internal static string CurrentIdentity => Scope + "." + Environment.ProcessId;

    internal SettingsChangeChannel(string identity, Action<AppearanceSettingsNotice> receive)
    {
        _identity = identity;
        _receive = receive;
        _listener = ListenAsync();
    }

    internal static async Task BroadcastAsync(AppearanceSettingsNotice notice)
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

    private static async Task SendAsync(string identity, AppearanceSettingsNotice notice)
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
                var notice = JsonSerializer.Deserialize<AppearanceSettingsNotice>(json);
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
