using System.Text.Json;
using OptiClaw.Core.Models;

namespace OptiClaw.Core.Services;

public sealed class OptiScalerInstaller(AppDataPaths paths)
{
    public static IReadOnlyList<string> SupportedProxyDllNames { get; } =
    [
        "dxgi.dll",
        "winmm.dll",
        "version.dll",
        "dbghelp.dll",
        "d3d12.dll",
        "wininet.dll",
        "winhttp.dll"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<InstallManifest> InstallAsync(
        GameProfile game,
        PreparedPayload payload,
        string proxyDllName,
        CancellationToken cancellationToken = default)
    {
        ValidateInstall(game, payload, proxyDllName);
        var manifest = new InstallManifest
        {
            GameId = game.Id,
            GameName = game.Name,
            DeploymentDirectory = Path.GetFullPath(game.DeploymentDirectory),
            OptiScalerVersion = payload.Version,
            ProxyDllName = proxyDllName
        };
        var installDataDirectory = paths.GetInstallDirectory(game.Id, manifest.Id);
        var originalsDirectory = Path.Combine(installDataDirectory, "Originals");
        var generatedDirectory = Path.Combine(installDataDirectory, "Generated");
        Directory.CreateDirectory(originalsDirectory);
        Directory.CreateDirectory(generatedDirectory);

        var generatedIni = Path.Combine(generatedDirectory, "OptiScaler.ini");
        await FileSystemHelpers.AtomicCopyAsync(
            Path.Combine(payload.DirectoryPath, "OptiScaler.ini"),
            generatedIni,
            cancellationToken).ConfigureAwait(false);
        await IniEditor.ConfigureXeSSAsync(generatedIni, cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var payloadFile in EnumeratePayloadFiles(payload.DirectoryPath, proxyDllName, generatedIni))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = GetSafeDestination(manifest.DeploymentDirectory, payloadFile.RelativePath);
                var change = new InstalledFileChange { RelativePath = payloadFile.RelativePath };

                if (File.Exists(destination))
                {
                    change.PreviousFileExisted = true;
                    change.PreviousSha256 = await FileSystemHelpers.ComputeSha256Async(destination, cancellationToken)
                        .ConfigureAwait(false);
                    change.BackupRelativePath = Path.Combine("Originals", payloadFile.RelativePath);
                    var backupPath = GetSafeDestination(installDataDirectory, change.BackupRelativePath);
                    await FileSystemHelpers.AtomicCopyAsync(destination, backupPath, cancellationToken).ConfigureAwait(false);
                }

                await FileSystemHelpers.AtomicCopyAsync(payloadFile.SourcePath, destination, cancellationToken)
                    .ConfigureAwait(false);
                change.InstalledSha256 = await FileSystemHelpers.ComputeSha256Async(destination, cancellationToken)
                    .ConfigureAwait(false);
                manifest.Files.Add(change);
            }

            await SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
            return manifest;
        }
        catch
        {
            await RollbackPartialInstallAsync(manifest, installDataDirectory).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<RestoreResult> RestoreAsync(
        Guid gameId,
        Guid installId,
        CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(gameId, installId, cancellationToken).ConfigureAwait(false);
        if (manifest.RestoredAt is not null)
        {
            return RestoreResult.Success;
        }

        var installDataDirectory = paths.GetInstallDirectory(gameId, installId);
        foreach (var change in manifest.Files.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = GetSafeDestination(manifest.DeploymentDirectory, change.RelativePath);
            if (change.PreviousFileExisted && change.BackupRelativePath is not null)
            {
                var backup = GetSafeDestination(installDataDirectory, change.BackupRelativePath);
                await FileSystemHelpers.AtomicCopyAsync(backup, destination, cancellationToken).ConfigureAwait(false);
            }
            else if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        RemoveEmptyCreatedDirectories(manifest);
        manifest.RestoredAt = DateTimeOffset.UtcNow;
        await SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
        DeleteRestoredPayload(installDataDirectory);
        return RestoreResult.Success;
    }

    public async Task<FrameGenerationSettings> LoadFrameGenerationSettingsAsync(
        Guid gameId,
        Guid installId,
        CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(gameId, installId, cancellationToken).ConfigureAwait(false);
        var iniPath = GetSafeDestination(manifest.DeploymentDirectory, "OptiScaler.ini");
        if (!File.Exists(iniPath))
        {
            throw new FileNotFoundException("The installed OptiScaler.ini could not be found.", iniPath);
        }

        return await IniEditor.ReadFrameGenerationAsync(iniPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateFrameGenerationSettingsAsync(
        Guid gameId,
        Guid installId,
        FrameGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(gameId, installId, cancellationToken).ConfigureAwait(false);
        if (manifest.RestoredAt is not null)
        {
            throw new InvalidOperationException("This OptiScaler installation has already been restored.");
        }

        var iniChange = manifest.Files.FirstOrDefault(change =>
            change.RelativePath.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The install manifest does not contain OptiScaler.ini.");
        var iniPath = GetSafeDestination(manifest.DeploymentDirectory, iniChange.RelativePath);
        if (!File.Exists(iniPath))
        {
            throw new FileNotFoundException("The installed OptiScaler.ini could not be found.", iniPath);
        }

        await IniEditor.ConfigureFrameGenerationAsync(iniPath, settings, cancellationToken).ConfigureAwait(false);
        iniChange.InstalledSha256 = await FileSystemHelpers.ComputeSha256Async(iniPath, cancellationToken)
            .ConfigureAwait(false);
        await SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstallManifest> LoadManifestAsync(
        Guid gameId,
        Guid installId,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(paths.GetInstallDirectory(gameId, installId), "manifest.json");
        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<InstallManifest>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The OptiClaw install manifest is empty.");
    }

    private async Task SaveManifestAsync(InstallManifest manifest, CancellationToken cancellationToken)
    {
        var installDirectory = paths.GetInstallDirectory(manifest.GameId, manifest.Id);
        Directory.CreateDirectory(installDirectory);
        var manifestPath = Path.Combine(installDirectory, "manifest.json");
        var temporaryPath = manifestPath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, manifestPath, true);
    }

    private static IEnumerable<PayloadFile> EnumeratePayloadFiles(
        string payloadDirectory,
        string proxyDllName,
        string generatedIni)
    {
        foreach (var file in Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(payloadDirectory, file);
            var fileName = Path.GetFileName(file);
            if (fileName.Equals(".ready", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("setup_windows.bat", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("setup_linux.sh", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("!! README_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
            {
                yield return new PayloadFile(file, proxyDllName);
            }
            else if (fileName.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase))
            {
                yield return new PayloadFile(generatedIni, "OptiScaler.ini");
            }
            else
            {
                yield return new PayloadFile(file, relativePath);
            }
        }
    }

    private static void ValidateInstall(GameProfile game, PreparedPayload payload, string proxyDllName)
    {
        if (game.ActiveInstallId is not null)
        {
            throw new InvalidOperationException("Restore the active OptiScaler installation before installing again.");
        }

        if (!SupportedProxyDllNames.Contains(proxyDllName, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(proxyDllName), proxyDllName, "Unsupported proxy DLL name.");
        }

        if (!Directory.Exists(game.DeploymentDirectory) || !File.Exists(game.ExecutablePath))
        {
            throw new DirectoryNotFoundException("The selected game executable or deployment directory no longer exists.");
        }

        if (!File.Exists(Path.Combine(payload.DirectoryPath, "OptiScaler.dll"))
            || !File.Exists(Path.Combine(payload.DirectoryPath, "OptiScaler.ini"))
            || !File.Exists(Path.Combine(payload.DirectoryPath, "libxess.dll")))
        {
            throw new InvalidDataException("The prepared payload is incomplete.");
        }
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes the expected directory: {relativePath}");
        }

        return destination;
    }

    private static async Task RollbackPartialInstallAsync(InstallManifest manifest, string installDataDirectory)
    {
        foreach (var change in manifest.Files.AsEnumerable().Reverse())
        {
            var destination = GetSafeDestination(manifest.DeploymentDirectory, change.RelativePath);
            if (change.PreviousFileExisted && change.BackupRelativePath is not null)
            {
                var backup = GetSafeDestination(installDataDirectory, change.BackupRelativePath);
                if (File.Exists(backup))
                {
                    await FileSystemHelpers.AtomicCopyAsync(backup, destination).ConfigureAwait(false);
                }
            }
            else if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }
    }

    private static void RemoveEmptyCreatedDirectories(InstallManifest manifest)
    {
        var directories = manifest.Files
            .Select(change => Path.GetDirectoryName(GetSafeDestination(manifest.DeploymentDirectory, change.RelativePath)))
            .Where(directory => directory is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(directory => directory!.Length);

        foreach (var directory in directories)
        {
            if (!string.Equals(directory, manifest.DeploymentDirectory, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private static void DeleteRestoredPayload(string installDirectory)
    {
        foreach (var directoryName in new[] { "Originals", "Generated" })
        {
            var directory = Path.Combine(installDirectory, directoryName);
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The restore succeeded; a future startup can retry stale backup cleanup.
            }
        }
    }

    private sealed record PayloadFile(string SourcePath, string RelativePath);
}
