namespace OptiClaw.Core.Models;

public sealed record OptiScalerRelease(
    string Version,
    string AssetName,
    Uri DownloadUri,
    long Size,
    string? Sha256);

public sealed record PreparedPayload(
    string Version,
    string DirectoryPath) : IDisposable
{
    private int _disposed;

    internal bool DeleteDirectoryOnDispose { get; init; }

    public void Dispose()
    {
        if (!DeleteDirectoryOnDispose || Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later preparation can remove an extraction left behind by a file lock.
        }
    }
}

