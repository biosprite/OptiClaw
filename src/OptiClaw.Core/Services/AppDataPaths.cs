namespace OptiClaw.Core.Services;

public sealed class AppDataPaths
{
    public AppDataPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OptiClaw");
    }

    public string Root { get; }
    public string ProfilesFile => Path.Combine(Root, "games.json");
    public string CacheDirectory => Path.Combine(Root, "Cache");
    public string BackupsDirectory => Path.Combine(Root, "Backups");

    public string GetInstallDirectory(Guid gameId, Guid installId) =>
        Path.Combine(BackupsDirectory, gameId.ToString("N"), installId.ToString("N"));
}

