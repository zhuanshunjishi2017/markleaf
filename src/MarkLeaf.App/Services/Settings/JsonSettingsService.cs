using System.Text.Json;
using MarkLeaf.Services.Logging;

namespace MarkLeaf.Services.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _settingsFile;
    private readonly IAppLogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public JsonSettingsService(string settingsFile, IAppLogger logger)
    {
        _settingsFile = settingsFile;
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFile))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = new FileStream(
                _settingsFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return settings?.SchemaVersion switch
            {
                AppSettings.CurrentSchemaVersion => NormalizeCurrent(settings),
                2 => MigrateVersion2(settings),
                1 => MigrateVersion1(settings),
                _ => new AppSettings(),
            };
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.Warning($"Settings could not be loaded: {exception.GetType().Name}.");
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_settingsFile)
            ?? throw new InvalidOperationException("Settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryFile = _settingsFile + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var stream = new FileStream(
            temporaryFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
            await JsonSerializer
                .SerializeAsync(stream, settings, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
            stream.Close();
            File.Move(temporaryFile, _settingsFile, true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    private static AppSettings MigrateVersion1(AppSettings settings)
    {
        var window = settings.MainWindow ?? new WindowSettings();
        window.Width = WindowPlacementCalculator.ToLogicalPixels(window.Width, window.Dpi);
        window.Height = WindowPlacementCalculator.ToLogicalPixels(window.Height, window.Dpi);
        window.WorkspaceWidth = WindowPlacementCalculator.ToLogicalPixels(window.WorkspaceWidth, window.Dpi);
        window.OutlineWidth = WindowPlacementCalculator.ToLogicalPixels(window.OutlineWidth, window.Dpi);
        settings.MainWindow = window;
        settings.Workspace ??= new WorkspaceSettings();
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return settings;
    }

    private static AppSettings MigrateVersion2(AppSettings settings)
    {
        settings.Workspace ??= new WorkspaceSettings();
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return settings;
    }

    private static AppSettings NormalizeCurrent(AppSettings settings)
    {
        settings.MainWindow ??= new WindowSettings();
        settings.Workspace ??= new WorkspaceSettings();
        settings.Workspace.RecentFolders ??= [];
        return settings;
    }
}
