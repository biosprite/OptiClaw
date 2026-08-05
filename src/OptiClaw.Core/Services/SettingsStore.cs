using System.Text.Json;
using OptiClaw.Core.Models;

namespace OptiClaw.Core.Services;

public sealed class SettingsStore(AppDataPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        if (!File.Exists(paths.SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(paths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(paths.Root);
        var temporaryPath = paths.SettingsFile + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, JsonOptions);
            stream.Flush(true);
        }

        File.Move(temporaryPath, paths.SettingsFile, true);
    }
}
