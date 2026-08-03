using OptiClaw.Core.Services;

namespace OptiClaw.Core.Tests;

public sealed class IniEditorTests
{
    [Fact]
    public async Task ConfigureXeSSAsync_ChangesEveryGraphicsApiInUpscalersSection()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.WriteFile("OptiScaler.ini", []);
        await File.WriteAllTextAsync(path, """
            [Upscalers]
            Dx11Upscaler=auto
            Dx12Upscaler=fsr22
            VulkanUpscaler=dlss

            [XeSS]
            BuildPipelines=true
            """);

        await IniEditor.ConfigureXeSSAsync(path);
        var configured = await File.ReadAllTextAsync(path);

        Assert.Contains("Dx11Upscaler=xess", configured);
        Assert.Contains("Dx12Upscaler=xess", configured);
        Assert.Contains("VulkanUpscaler=xess", configured);
        Assert.Contains("[XeSS]", configured);
    }
}

