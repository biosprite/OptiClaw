using OptiClaw.Core.Models;

namespace OptiClaw.Core.Services;

public static class IniEditor
{
    private static readonly HashSet<string> FrameGenerationInputs = new(StringComparer.OrdinalIgnoreCase)
        { "dlssg", "fsrfg", "upscaler" };

    private static readonly HashSet<string> FrameGenerationOutputs = new(StringComparer.OrdinalIgnoreCase)
        { "xefg" };

    public static async Task ConfigureXeSSAsync(string iniPath, CancellationToken cancellationToken = default)
    {
        var lines = (await File.ReadAllLinesAsync(iniPath, cancellationToken).ConfigureAwait(false)).ToList();
        var inUpscalers = false;
        var upscalersStart = -1;
        var upscalersEnd = lines.Count;
        var foundDx11 = false;
        var foundDx12 = false;
        var foundVulkan = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inUpscalers)
                {
                    upscalersEnd = index;
                }

                inUpscalers = trimmed.Equals("[Upscalers]", StringComparison.OrdinalIgnoreCase);
                if (inUpscalers)
                {
                    upscalersStart = index;
                }
                continue;
            }

            if (!inUpscalers || trimmed.StartsWith(';'))
            {
                continue;
            }

            if (TryReplace(lines, index, "Dx11Upscaler", "xess"))
            {
                foundDx11 = true;
            }
            else if (TryReplace(lines, index, "Dx12Upscaler", "xess"))
            {
                foundDx12 = true;
            }
            else if (TryReplace(lines, index, "VulkanUpscaler", "xess"))
            {
                foundVulkan = true;
            }
        }

        if (!foundDx11 || !foundDx12 || !foundVulkan)
        {
            var additions = new List<string>();
            if (!foundDx11) additions.Add("Dx11Upscaler=xess");
            if (!foundDx12) additions.Add("Dx12Upscaler=xess");
            if (!foundVulkan) additions.Add("VulkanUpscaler=xess");
            if (upscalersStart >= 0)
            {
                lines.InsertRange(upscalersEnd, additions);
            }
            else
            {
                lines.AddRange([string.Empty, "; Added by OptiClaw", "[Upscalers]", .. additions]);
            }
        }

        await File.WriteAllLinesAsync(iniPath, lines, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<FrameGenerationSettings> ReadFrameGenerationAsync(
        string iniPath,
        CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(iniPath, cancellationToken).ConfigureAwait(false);
        var enabled = string.Equals(
            GetSectionValue(lines, "FrameGen", "Enabled"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var input = GetSectionValue(lines, "FrameGen", "FGInput");
        var output = GetSectionValue(lines, "FrameGen", "FGOutput");
        var interpolationValue = GetSectionValue(lines, "XeFG", "InterpolationCount");

        if (input is null || !FrameGenerationInputs.Contains(input))
        {
            input = "upscaler";
        }

        if (output is null || !FrameGenerationOutputs.Contains(output))
        {
            output = "xefg";
        }

        var interpolationCount = int.TryParse(interpolationValue, out var parsedCount)
            && parsedCount is >= 1 and <= 3
                ? parsedCount
                : 1;

        return new FrameGenerationSettings(enabled, input, output, interpolationCount);
    }

    public static async Task ConfigureFrameGenerationAsync(
        string iniPath,
        FrameGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!FrameGenerationInputs.Contains(settings.Input))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Input, "Unsupported frame-generation input.");
        }

        if (!FrameGenerationOutputs.Contains(settings.Output))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Output, "Unsupported frame-generation output.");
        }

        if (settings.InterpolationCount is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.InterpolationCount, "Interpolation count must be from 1 to 3.");
        }

        var lines = (await File.ReadAllLinesAsync(iniPath, cancellationToken).ConfigureAwait(false)).ToList();
        SetSectionValues(lines, "FrameGen", new Dictionary<string, string>
        {
            ["Enabled"] = settings.Enabled ? "true" : "false",
            ["FGInput"] = settings.Input,
            ["FGOutput"] = settings.Output
        });
        SetSectionValues(lines, "XeFG", new Dictionary<string, string>
        {
            ["InterpolationCount"] = settings.InterpolationCount.ToString()
        });

        await File.WriteAllLinesAsync(iniPath, lines, cancellationToken).ConfigureAwait(false);
    }

    private static string? GetSectionValue(IReadOnlyList<string> lines, string sectionName, string key)
    {
        var inSection = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inSection = trimmed.Equals($"[{sectionName}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection || trimmed.StartsWith(';'))
            {
                continue;
            }

            var equals = trimmed.IndexOf('=');
            if (equals >= 0 && trimmed[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(equals + 1)..].Trim();
            }
        }

        return null;
    }

    private static void SetSectionValues(
        List<string> lines,
        string sectionName,
        IReadOnlyDictionary<string, string> values)
    {
        var sectionStart = lines.FindIndex(line =>
            line.Trim().Equals($"[{sectionName}]", StringComparison.OrdinalIgnoreCase));
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.Add($"[{sectionName}]");
            foreach (var value in values)
            {
                lines.Add($"{value.Key}={value.Value}");
            }
            return;
        }

        var sectionEnd = lines.FindIndex(
            sectionStart + 1,
            line => line.Trim().StartsWith('[') && line.Trim().EndsWith(']'));
        if (sectionEnd < 0)
        {
            sectionEnd = lines.Count;
        }

        foreach (var value in values)
        {
            var found = false;
            for (var index = sectionStart + 1; index < sectionEnd; index++)
            {
                if (lines[index].TrimStart().StartsWith(';') || !TryReplace(lines, index, value.Key, value.Value))
                {
                    continue;
                }

                found = true;
                break;
            }

            if (!found)
            {
                lines.Insert(sectionEnd, $"{value.Key}={value.Value}");
                sectionEnd++;
            }
        }
    }

    private static bool TryReplace(List<string> lines, int index, string key, string value)
    {
        var equals = lines[index].IndexOf('=');
        if (equals < 0 || !lines[index][..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var indentation = lines[index][..(lines[index].Length - lines[index].TrimStart().Length)];
        lines[index] = $"{indentation}{key}={value}";
        return true;
    }
}
