namespace OptiClaw.Core.Models;

public sealed record GameDetectionResult(
    string Name,
    string InstallDirectory,
    string ExecutablePath,
    string DeploymentDirectory,
    IReadOnlyList<string> DetectedTechnologies,
    GameSource Source)
{
    public GameProfile ToProfile() => new()
    {
        Name = Name,
        InstallDirectory = InstallDirectory,
        ExecutablePath = ExecutablePath,
        DeploymentDirectory = DeploymentDirectory,
        DetectedTechnologies = [.. DetectedTechnologies],
        Source = Source,
        LastScannedAt = DateTimeOffset.UtcNow
    };
}

