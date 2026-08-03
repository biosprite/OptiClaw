using System.Security.Cryptography;

namespace OptiClaw.Core.Services;

internal static class FileSystemHelpers
{
    public static IEnumerable<string> EnumerateFilesSafe(
        string root,
        Func<string, bool>? include = null,
        int maxFiles = 30_000)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        var seen = 0;

        while (pending.Count > 0 && seen < maxFiles)
        {
            var current = pending.Pop();
            IEnumerable<string> files = [];
            IEnumerable<string> directories = [];

            try
            {
                files = Directory.EnumerateFiles(current);
                directories = Directory.EnumerateDirectories(current);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (++seen > maxFiles)
                {
                    yield break;
                }

                if (include is null || include(file))
                {
                    yield return file;
                }
            }

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (!IgnoredDirectoryNames.Contains(name))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task AtomicCopyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"No parent directory for {destination}.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.opticlaw.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true))
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        ".git",
        "node_modules",
        "_CommonRedist",
        "Redist",
        "Redistributables",
        "EasyAntiCheat",
        "EasyAntiCheat_EOS",
        "BattlEye"
    };
}

