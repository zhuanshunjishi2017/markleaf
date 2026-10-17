namespace MarkLeaf.Services.Settings;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        AppSettings settings,
        SettingsSaveScope scope = SettingsSaveScope.Full,
        CancellationToken cancellationToken = default);
}

