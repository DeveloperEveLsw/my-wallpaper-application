using System.Text.Json;

namespace Wallpaper.Infrastructure.Windows.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public JsonAppSettingsStore(string? settingsDirectory = null)
    {
        var directory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyWallpaperApplication");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public async Task<AppSettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettingsLoadResult(AppSettings.Default, WasCorrupted: false);
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (settings is null || settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
            {
                return new AppSettingsLoadResult(AppSettings.Default, WasCorrupted: true);
            }

            return new AppSettingsLoadResult(settings, WasCorrupted: false);
        }
        catch (JsonException)
        {
            return new AppSettingsLoadResult(AppSettings.Default, WasCorrupted: true);
        }
        catch (IOException)
        {
            return new AppSettingsLoadResult(AppSettings.Default, WasCorrupted: true);
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettingsLoadResult(AppSettings.Default, WasCorrupted: true);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
