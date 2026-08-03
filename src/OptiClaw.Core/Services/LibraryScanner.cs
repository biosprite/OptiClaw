using System.Text.RegularExpressions;
using Microsoft.Win32;
using OptiClaw.Core.Models;

namespace OptiClaw.Core.Services;

public sealed partial class LibraryScanner(GameDetector detector)
{
    public async Task<IReadOnlyList<GameDetectionResult>> ScanInstalledLibrariesAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var installations = new Dictionary<string, (string Name, GameSource Source)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, name) in FindSteamGames())
        {
            installations[path] = (name, GameSource.Steam);
        }

        foreach (var path in FindXboxGames())
        {
            installations.TryAdd(path, (Path.GetFileName(path), GameSource.Xbox));
        }

        var results = new List<GameDetectionResult>();
        foreach (var installation in installations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Scanning {installation.Value.Name}…");
            var result = await detector.ScanAsync(
                installation.Key,
                displayName: installation.Value.Name,
                source: installation.Value.Source,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result is not null && result.DetectedTechnologies.Count > 0)
            {
                results.Add(result);
            }
        }

        return results.OrderBy(result => result.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<GameDetectionResult>> ScanFolderAsync(
        string root,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directories = Directory.EnumerateDirectories(root).ToArray();
        if (directories.Length == 0)
        {
            directories = [root];
        }

        var results = new List<GameDetectionResult>();
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Scanning {Path.GetFileName(directory)}…");
            var result = await detector.ScanAsync(directory, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result is not null && result.DetectedTechnologies.Count > 0)
            {
                results.Add(result);
            }
        }

        if (results.Count == 0
            && (directories.Length != 1
                || !string.Equals(root, directories[0], StringComparison.OrdinalIgnoreCase)))
        {
            var result = await detector.ScanAsync(root, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    private static IEnumerable<(string Path, string Name)> FindSteamGames()
    {
        foreach (var steamRoot in GetSteamRoots())
        {
            foreach (var library in GetSteamLibraries(steamRoot))
            {
                var steamApps = Path.Combine(library, "steamapps");
                IEnumerable<string> manifests;
                try
                {
                    manifests = Directory.EnumerateFiles(steamApps, "appmanifest_*.acf").ToArray();
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    continue;
                }

                foreach (var manifest in manifests)
                {
                    string contents;
                    try
                    {
                        contents = File.ReadAllText(manifest);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    var installDirectory = ReadVdfValue(contents, "installdir");
                    if (string.IsNullOrWhiteSpace(installDirectory))
                    {
                        continue;
                    }

                    var path = Path.Combine(steamApps, "common", installDirectory);
                    if (Directory.Exists(path))
                    {
                        var name = ReadVdfValue(contents, "name") ?? installDirectory;
                        yield return (path, name);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var registryPath = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam",
                    "SteamPath",
                    null) as string;
                if (!string.IsNullOrWhiteSpace(registryPath))
                {
                    candidates.Add(registryPath.Replace('/', Path.DirectorySeparatorChar));
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                // Fall back to standard paths.
            }
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        return candidates.Where(Directory.Exists);
    }

    private static IEnumerable<string> GetSteamLibraries(string steamRoot)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            try
            {
                var contents = File.ReadAllText(libraryFile);
                foreach (Match match in VdfPathRegex().Matches(contents))
                {
                    var path = Regex.Unescape(match.Groups[1].Value).Replace("\\\\", "\\");
                    if (Directory.Exists(path))
                    {
                        libraries.Add(path);
                    }
                }
            }
            catch (IOException)
            {
                // The default library is still usable.
            }
        }

        return libraries;
    }

    private static IEnumerable<string> FindXboxGames()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            var root = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
            IEnumerable<string> gameDirectories;
            try
            {
                gameDirectories = Directory.EnumerateDirectories(root).ToArray();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var gameDirectory in gameDirectories)
            {
                var content = Path.Combine(gameDirectory, "Content");
                yield return Directory.Exists(content) ? content : gameDirectory;
            }
        }
    }

    private static string? ReadVdfValue(string contents, string key)
    {
        var match = Regex.Match(
            contents,
            $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]+)\\\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? Regex.Unescape(match.Groups[1].Value) : null;
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VdfPathRegex();
}
