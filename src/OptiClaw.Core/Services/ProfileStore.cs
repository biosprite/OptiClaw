using System.Text.Json;
using OptiClaw.Core.Models;

namespace OptiClaw.Core.Services;

public sealed class ProfileStore(AppDataPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<GameProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.ProfilesFile))
        {
            return [];
        }

        await using var stream = File.OpenRead(paths.ProfilesFile);
        return await JsonSerializer.DeserializeAsync<List<GameProfile>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    public async Task SaveAsync(IEnumerable<GameProfile> games, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.Root);
        var temporaryPath = paths.ProfilesFile + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, games, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, paths.ProfilesFile, true);
    }
}

