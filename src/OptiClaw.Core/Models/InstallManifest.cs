namespace OptiClaw.Core.Models;

public sealed class InstallManifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string DeploymentDirectory { get; set; } = string.Empty;
    public string OptiScalerVersion { get; set; } = string.Empty;
    public string ProxyDllName { get; set; } = "dxgi.dll";
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RestoredAt { get; set; }
    public List<InstalledFileChange> Files { get; set; } = [];
}

public sealed class InstalledFileChange
{
    public string RelativePath { get; set; } = string.Empty;
    public bool PreviousFileExisted { get; set; }
    public string? PreviousSha256 { get; set; }
    public string? BackupRelativePath { get; set; }
    public string InstalledSha256 { get; set; } = string.Empty;
}

public sealed record RestoreResult(bool Succeeded, IReadOnlyList<string> Conflicts)
{
    public static RestoreResult Success { get; } = new(true, []);
}

