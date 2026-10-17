using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.App;

internal sealed class SharedSettingsState
{
    public FileSettings File { get; set; } = new();
    public EditorSettings Editor { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public ImageSettings Image { get; set; } = new();
    public ExportSettings Export { get; set; } = new();
    public ShortcutSettings Shortcut { get; set; } = new();
    public CodeFormatterSettings CodeFormatter { get; set; } = new();
    public string MarkdownStyle { get; set; } = "serif";
    public string ColorTheme { get; set; } = "apple-blue";
}

internal sealed record SharedSettingsNotice(SharedSettingsState Shared)
{
    public static SharedSettingsNotice From(AppSettings settings)
    {
        var shared = new SharedSettingsState
        {
            File = CloneSharedSection(settings.File),
            Editor = CloneSharedSection(settings.Editor),
            Appearance = CloneSharedSection(settings.Appearance),
            General = CloneSharedSection(settings.General),
            Image = CloneSharedSection(settings.Image),
            Export = CloneSharedSection(settings.Export),
            Shortcut = CloneSharedSection(settings.Shortcut),
            CodeFormatter = CloneSharedSection(settings.CodeFormatter),
            MarkdownStyle = settings.MarkdownStyle,
            ColorTheme = settings.ColorTheme,
        };
        return new(shared);
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.File = CloneSharedSection(Shared.File);
        settings.Editor = CloneSharedSection(Shared.Editor);
        settings.Appearance = CloneSharedSection(Shared.Appearance);
        settings.General = CloneSharedSection(Shared.General);
        settings.Image = CloneSharedSection(Shared.Image);
        settings.Export = CloneSharedSection(Shared.Export);
        settings.Shortcut.ApplyFrom(CloneSharedSection(Shared.Shortcut));
        settings.CodeFormatter.ApplyFrom(CloneSharedSection(Shared.CodeFormatter));
        settings.MarkdownStyle = Shared.MarkdownStyle;
        settings.ColorTheme = Shared.ColorTheme;
    }

    private static T CloneSharedSection<T>(T source) where T : class
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, source, options);
        stream.Position = 0;
        return JsonSerializer.Deserialize<T>(stream, options)
            ?? throw new InvalidOperationException("Could not clone shared settings section.");
    }
}

internal sealed record RecentHistoryNotice(IReadOnlyList<string> RecentFiles, IReadOnlyList<string> RecentFolders)
{
    public RecentHistoryNotice(IEnumerable<string> recentFiles, IEnumerable<string> recentFolders)
        : this(recentFiles.ToArray(), recentFolders.ToArray()) { }
}

internal sealed record SettingsPeerNotice(
    SharedSettingsNotice? SharedSettings,
    RecentHistoryNotice? RecentHistory);

// Every Windows window owns a process. Peer notices carry shared ownership only;
// MainWindow and document-restore state are never broadcast.
internal sealed class SettingsChangeChannel : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _identity;
    private readonly Action<SettingsPeerNotice> _receive;
    private readonly Task _listener;
    private static readonly string Scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        (Environment.ProcessPath ?? "MarkLeaf") + ":" + Process.GetCurrentProcess().SessionId)))[..16];
    internal static string CurrentIdentity => Scope + "." + Environment.ProcessId;

    internal SettingsChangeChannel(string identity, Action<SettingsPeerNotice> receive)
    {
        _identity = identity;
        _receive = receive;
        _listener = ListenAsync();
    }

    internal static async Task BroadcastAsync(SettingsPeerNotice notice)
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

    private static async Task SendAsync(string identity, SettingsPeerNotice notice)
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
                var notice = JsonSerializer.Deserialize<SettingsPeerNotice>(json);
                if (notice is not null && (notice.SharedSettings is not null || notice.RecentHistory is not null))
                {
                    _receive(notice);
                }
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
