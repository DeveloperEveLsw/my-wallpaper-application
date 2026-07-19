using Wallpaper.Infrastructure.Windows.Settings;

namespace Wallpaper.Infrastructure.Windows.Tests;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoad_RoundTripsCurrentSettingsWithoutTemporaryFiles()
    {
        var store = new JsonAppSettingsStore(_settingsDirectory);
        var settings = new AppSettings(
            AppSettings.CurrentSchemaVersion,
            RootPath: Path.GetFullPath("fixture-root"),
            FolderOrder: ["Work", "Photos"]);

        await store.SaveAsync(settings);
        var result = await store.LoadAsync();

        Assert.False(result.WasCorrupted);
        Assert.Equal(settings.SchemaVersion, result.Settings.SchemaVersion);
        Assert.Equal(settings.RootPath, result.Settings.RootPath);
        Assert.Equal(settings.FolderOrder, result.Settings.FolderOrder);
        Assert.Empty(Directory.EnumerateFiles(_settingsDirectory, "*.tmp"));
    }

    [Fact]
    public async Task Load_ReturnsRecoverableDefaultWhenJsonIsCorrupted()
    {
        Directory.CreateDirectory(_settingsDirectory);
        await File.WriteAllTextAsync(Path.Combine(_settingsDirectory, "settings.json"), "{invalid");
        var store = new JsonAppSettingsStore(_settingsDirectory);

        var result = await store.LoadAsync();

        Assert.True(result.WasCorrupted);
        Assert.Equal(AppSettings.Default, result.Settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }
}
