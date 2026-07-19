namespace Wallpaper.Infrastructure.Windows.Settings;

public interface IAppSettingsStore
{
    Task<AppSettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public sealed record AppSettingsLoadResult(AppSettings Settings, bool WasCorrupted);
