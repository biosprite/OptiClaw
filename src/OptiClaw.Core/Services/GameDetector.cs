using OptiClaw.Core.Models;

namespace OptiClaw.Core.Services;

public sealed class GameDetector
{
    private static readonly IReadOnlyDictionary<string, string> TechnologyDlls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nvngx_dlss.dll"] = "DLSS",
            ["sl.interposer.dll"] = "NVIDIA Streamline",
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
            }
        }

        if (preferredExecutable is not null && File.Exists(preferredExecutable)
            && !executables.Contains(preferredExecutable, StringComparer.OrdinalIgnoreCase))
        {
            executables.Add(Path.GetFullPath(preferredExecutable));
        }

        var executable = executables
            .OrderByDescending(path => ScoreExecutable(path, normalizedRoot, preferredExecutable))
            .ThenBy(path => path.Length)
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

    private static int ScoreExecutable(string path, string root, string? preferredExecutable)
    {
        var score = 0;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var normalized = path.Replace('/', '\\');

        if (preferredExecutable is not null
            && string.Equals(Path.GetFullPath(path), Path.GetFullPath(preferredExecutable), StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (fileName.Contains("shipping", StringComparison.OrdinalIgnoreCase))
        {
            score += 140;
        }

        if (normalized.Contains("\\Binaries\\Win64\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\Binaries\\WinGDK\\", StringComparison.OrdinalIgnoreCase))
        {
            score += 90;
        }

        if (normalized.Contains("\\bin\\x64", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\Retail\\", StringComparison.OrdinalIgnoreCase))
        {
            score += 55;
        }

        var rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (NormalizeName(fileName).Contains(NormalizeName(rootName), StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        if (IsNonGameExecutable(path, fileName))
        {
            score -= 300;
        }

        try
        {
            var length = new FileInfo(path).Length;
            score += length switch
            {
                > 50 * 1024 * 1024 => 30,
                > 10 * 1024 * 1024 => 20,
                > 1024 * 1024 => 10,
                _ => 0
            };
        }
        catch (IOException)
        {
            // Scoring still works when metadata is temporarily unavailable.
        }

        return score;
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
}

