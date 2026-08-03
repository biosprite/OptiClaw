using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptiClaw.Core.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace OptiClaw.Core.Services;

public sealed class OptiScalerReleaseClient : IDisposable
{
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/optiscaler/OptiScaler/releases/latest");

    private readonly AppDataPaths _paths;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public OptiScalerReleaseClient(AppDataPaths paths, HttpClient? httpClient = null)
    {
        _paths = paths;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OptiClaw", "0.1"));
        }
    }

    public async Task<OptiScalerRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await _httpClient.GetStreamAsync(LatestReleaseUri, cancellationToken)
            .ConfigureAwait(false);
        var response = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("GitHub returned an empty OptiScaler release response.");

        var asset = response.Assets.FirstOrDefault(item =>
                item.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                && item.Name.Contains("Optiscaler", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The latest OptiScaler release has no supported .7z payload.");

        var digest = asset.Digest;
        var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? digest["sha256:".Length..].ToLowerInvariant()
            : null;

        return new OptiScalerRelease(
            response.TagName.TrimStart('v'),
            asset.Name,
            new Uri(asset.DownloadUrl),
            asset.Size,
            sha256);
    }

    public async Task<PreparedPayload> PrepareLatestAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        var versionDirectory = Path.Combine(_paths.CacheDirectory, release.Version);
        var payloadDirectory = Path.Combine(versionDirectory, "payload");
        var markerPath = Path.Combine(payloadDirectory, ".ready");
        if (File.Exists(markerPath) && File.Exists(Path.Combine(payloadDirectory, "OptiScaler.dll")))
        {
            progress?.Report(1);
            return new PreparedPayload(release.Version, payloadDirectory);
        }

        Directory.CreateDirectory(versionDirectory);
        var archivePath = Path.Combine(versionDirectory, release.AssetName);
        await DownloadAndVerifyAsync(release, archivePath, progress, cancellationToken).ConfigureAwait(false);

        var extractionDirectory = Path.Combine(versionDirectory, $"extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractionDirectory);
        try
        {
            ExtractArchive(archivePath, extractionDirectory);
            if (!File.Exists(Path.Combine(extractionDirectory, "OptiScaler.dll"))
                || !File.Exists(Path.Combine(extractionDirectory, "OptiScaler.ini"))
                || !File.Exists(Path.Combine(extractionDirectory, "libxess.dll")))
            {
                throw new InvalidDataException("The downloaded archive is missing required OptiScaler/XeSS files.");
            }

            await File.WriteAllTextAsync(
                Path.Combine(extractionDirectory, ".ready"),
                $"OptiScaler {release.Version}",
                cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(payloadDirectory))
            {
                Directory.Delete(payloadDirectory, true);
            }

            Directory.Move(extractionDirectory, payloadDirectory);
        }
        finally
        {
            if (Directory.Exists(extractionDirectory))
            {
                Directory.Delete(extractionDirectory, true);
            }
        }

        progress?.Report(1);
        return new PreparedPayload(release.Version, payloadDirectory);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task DownloadAndVerifyAsync(
        OptiScalerRelease release,
        string archivePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(archivePath)
            && (release.Sha256 is null
                || string.Equals(
                    await FileSystemHelpers.ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false),
                    release.Sha256,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var temporaryPath = archivePath + ".download";
        try
        {
            using var response = await _httpClient.GetAsync(
                release.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? release.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[1024 * 128];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloaded += read;
                if (totalBytes > 0)
                {
                    progress?.Report(Math.Min(0.9, downloaded / (double)totalBytes * 0.9));
                }
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            var actualSha256 = await FileSystemHelpers.ComputeSha256Async(temporaryPath, cancellationToken)
                .ConfigureAwait(false);
            if (release.Sha256 is not null
                && !string.Equals(actualSha256, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"OptiScaler download verification failed. Expected {release.Sha256}, got {actualSha256}.");
            }

            File.Move(temporaryPath, archivePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ExtractArchive(string archivePath, string destination)
    {
        using var archive = ArchiveFactory.Open(archivePath);
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries.Where(item => !item.IsDirectory))
        {
            var entryKey = entry.Key
                ?? throw new InvalidDataException("An OptiScaler archive entry has no path.");
            var key = entryKey.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(destination, key));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe path in OptiScaler archive: {entryKey}");
            }

            entry.WriteToDirectory(destination, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true,
                PreserveFileTime = true
            });
        }
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAssetResponse> Assets { get; set; } = [];
    }

    private sealed class GitHubAssetResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
