using System.Text.Json;
using OptiClaw.Core.Models;
using OptiClaw.Core.Services;

namespace OptiClaw.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void LoadReturnsSystemThemeDefaultsWhenSettingsDoNotExist()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(new AppDataPaths(temporary.Path));

        var settings = store.Load();

        Assert.Equal(1, settings.SchemaVersion);
        Assert.Equal("Default", settings.Theme);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
        Assert.False(settings.WindowMaximized);
    }

    [Fact]
    public void SaveAndLoadRoundTripsExpandableSettingsDocument()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppDataPaths(temporary.Path);
        var store = new SettingsStore(paths);

        store.Save(new AppSettings
        {
            Theme = "Dark",
            WindowWidth = 1216.5,
            WindowHeight = 768.25,
            WindowMaximized = true
        });
        var settings = store.Load();

        Assert.Equal(1, settings.SchemaVersion);
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal(1216.5, settings.WindowWidth);
        Assert.Equal(768.25, settings.WindowHeight);
        Assert.True(settings.WindowMaximized);
        using var document = JsonDocument.Parse(File.ReadAllText(paths.SettingsFile));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Dark", document.RootElement.GetProperty("theme").GetString());
        Assert.Equal(1216.5, document.RootElement.GetProperty("windowWidth").GetDouble());
        Assert.Equal(768.25, document.RootElement.GetProperty("windowHeight").GetDouble());
        Assert.True(document.RootElement.GetProperty("windowMaximized").GetBoolean());
    }

    [Fact]
    public void LoadUsesDefaultsForInvalidJson()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppDataPaths(temporary.Path);
        File.WriteAllText(paths.SettingsFile, "not-json");

        var settings = new SettingsStore(paths).Load();

        Assert.Equal("Default", settings.Theme);
    }
}
