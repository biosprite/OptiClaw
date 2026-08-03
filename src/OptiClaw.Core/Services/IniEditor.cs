namespace OptiClaw.Core.Services;

public static class IniEditor
{
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
