using OptiClaw.Core.Models;

namespace OptiClaw.Core.Services;

public sealed class GameDetector
{
    private static readonly IReadOnlyDictionary<string, string> TechnologyDlls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nvngx_dlss.dll"] = "DLSS",
            ["sl.interposer.dll"] = "NVIDIA FG",
            ["libxess.dll"] = "XeSS",
            ["libxess_dx11.dll"] = "XeSS",
            ["ffx_fsr2_api_dx12_x64.dll"] = "FSR 2",
            ["ffx_fsr2_api_vk_x64.dll"] = "FSR 2",
            ["ffx_fsr2_api_x64.dll"] = "FSR 2",
            ["amd_fidelityfx_dx12.dll"] = "FidelityFX / FSR",
            ["amd_fidelityfx_upscaler_dx12.dll"] = "FidelityFX / FSR",
            ["amd_fidelityfx_vk.dll"] = "FidelityFX / FSR"
        };

    public Task<GameDetectionResult?> ScanAsync(
        string installDirectory,
        string? preferredExecutable = null,
        string? displayName = null,
        GameSource source = GameSource.Custom,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(installDirectory, preferredExecutable, displayName, source, cancellationToken), cancellationToken);

    private static GameDetectionResult? Scan(
        string installDirectory,
        string? preferredExecutable,
        string? displayName,
        GameSource source,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = Path.GetFullPath(installDirectory);
        if (!Directory.Exists(normalizedRoot))
        {
            return null;
        }

        var executables = new List<string>();
        var technologies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var technologyDlls = new List<string>();

        foreach (var file in FileSystemHelpers.EnumerateFilesSafe(
                     normalizedRoot,
                     path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                executables.Add(file);
            }
            else if (TechnologyDlls.TryGetValue(fileName, out var technology))
            {
                technologies.Add(technology);
                technologyDlls.Add(file);
            }
        }

        if (preferredExecutable is not null && File.Exists(preferredExecutable)
            && !executables.Contains(preferredExecutable, StringComparer.OrdinalIgnoreCase))
        {
            executables.Add(Path.GetFullPath(preferredExecutable));
        }

        var candidates = executables
            .Select(path => CreateCandidate(path, normalizedRoot, preferredExecutable, technologyDlls))
            .ToArray();
        var eligibleCandidates = candidates.Where(candidate => !candidate.IsHelper).ToArray();

        var executable = RankCandidates(eligibleCandidates.Length > 0 ? eligibleCandidates : candidates)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();

        if (executable is null)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? MakeDisplayName(Path.GetFileNameWithoutExtension(executable))
            : displayName.Trim();

        return new GameDetectionResult(
            name,
            normalizedRoot,
            executable,
            Path.GetDirectoryName(executable)!,
            technologies.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            source);
    }

    private static IOrderedEnumerable<ExecutableCandidate> RankCandidates(
        IEnumerable<ExecutableCandidate> candidates) =>
        candidates
            .OrderByDescending(candidate => candidate.IsPreferred)
            .ThenByDescending(candidate => candidate.Layout)
            .ThenBy(candidate => candidate.TechnologyDistance)
            .ThenByDescending(candidate => candidate.NameAffinity)
            .ThenByDescending(candidate => candidate.FileLength)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase);

    private static ExecutableCandidate CreateCandidate(
        string path,
        string root,
        string? preferredExecutable,
        IReadOnlyList<string> technologyDlls)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
        var normalizedFileName = NormalizeName(fileName);
        var normalizedRootName = NormalizeName(rootName);

        return new ExecutableCandidate(
            path,
            preferredExecutable is not null
                && string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(preferredExecutable),
                    StringComparison.OrdinalIgnoreCase),
            IsNonGameExecutable(path, fileName),
            GetLayout(path, root),
            GetTechnologyDistance(path, technologyDlls),
            GetNameAffinity(normalizedFileName, normalizedRootName),
            GetFileLength(path));
    }

    private static ExecutableLayout GetLayout(string path, string root)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var segments = Path.GetRelativePath(root, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var isShipping = fileName.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_Win64_Shipping", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("-WinGDK-Shipping", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_WinGDK_Shipping", StringComparison.OrdinalIgnoreCase);
        var isUnrealBinary = ContainsAdjacentSegments(segments, "Binaries", "Win64")
            || ContainsAdjacentSegments(segments, "Binaries", "WinGDK");

        if (isShipping && isUnrealBinary)
        {
            return ExecutableLayout.UnrealShipping;
        }

        if (isUnrealBinary)
        {
            return ExecutableLayout.PlatformBinary;
        }

        if (ContainsAdjacentSegments(segments, "bin", "x64")
            || segments.Contains("Retail", StringComparer.OrdinalIgnoreCase))
        {
            return ExecutableLayout.ConventionalGameDirectory;
        }

        return ExecutableLayout.Unknown;
    }

    private static bool ContainsAdjacentSegments(string[] segments, string parent, string child)
    {
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], parent, StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index + 1], child, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetTechnologyDistance(string executable, IReadOnlyList<string> technologyDlls)
    {
        if (technologyDlls.Count == 0)
        {
            return int.MaxValue;
        }

        var executableDirectory = Path.GetDirectoryName(executable)!;
        return technologyDlls.Min(dll => GetDirectoryDistance(executableDirectory, Path.GetDirectoryName(dll)!));
    }

    private static int GetDirectoryDistance(string first, string second)
    {
        var firstSegments = Path.GetFullPath(first)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var secondSegments = Path.GetFullPath(second)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var sharedSegments = 0;

        while (sharedSegments < firstSegments.Length
               && sharedSegments < secondSegments.Length
               && string.Equals(firstSegments[sharedSegments], secondSegments[sharedSegments], StringComparison.OrdinalIgnoreCase))
        {
            sharedSegments++;
        }

        return firstSegments.Length + secondSegments.Length - (2 * sharedSegments);
    }

    private static int GetNameAffinity(string fileName, string rootName)
    {
        if (fileName.Length == 0 || rootName.Length == 0)
        {
            return 0;
        }

        if (string.Equals(fileName, rootName, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return fileName.Contains(rootName, StringComparison.OrdinalIgnoreCase)
            || rootName.Contains(fileName, StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static bool IsNonGameExecutable(string path, string fileName)
    {
        var normalized = path.Replace('/', '\\');
        return fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("crashreport", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("benchmark", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("bootstrap", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("launcher", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\Engine\\Binaries\\ThirdParty\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\Engine\\Extras\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeDisplayName(string name)
    {
        var cleaned = name
            .Replace("-Win64-Shipping", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_Win64_Shipping", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .Replace('-', ' ');
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private enum ExecutableLayout
    {
        Unknown,
        ConventionalGameDirectory,
        PlatformBinary,
        UnrealShipping
    }

    private sealed record ExecutableCandidate(
        string Path,
        bool IsPreferred,
        bool IsHelper,
        ExecutableLayout Layout,
        int TechnologyDistance,
        int NameAffinity,
        long FileLength);
}

