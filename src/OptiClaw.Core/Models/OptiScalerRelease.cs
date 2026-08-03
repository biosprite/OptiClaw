namespace OptiClaw.Core.Models;

public sealed record OptiScalerRelease(
    string Version,
    string AssetName,
    Uri DownloadUri,
    long Size,
    string? Sha256);

public sealed record PreparedPayload(
    string Version,
    string DirectoryPath);

