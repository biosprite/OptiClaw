using OptiClaw.Core.Models;
using OptiClaw.Core.Services;

namespace OptiClaw.Core.Tests;

public sealed class GameDetectorTests
{
    [Fact]
    public async Task ScanAsync_PrefersUnrealShippingExecutableAndFindsDlss()
    {
        using var temporary = new TemporaryDirectory();
        var launcher = temporary.WriteFile("Launcher.exe", new byte[128]);
        var shipping = temporary.WriteFile(
            @"CoolGame\Binaries\Win64\CoolGame-Win64-Shipping.exe",
            new byte[2 * 1024 * 1024]);
        temporary.WriteFile(@"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64\nvngx_dlss.dll");

        var result = await new GameDetector().ScanAsync(
            temporary.Path,
            launcher,
            "Cool Game",
            GameSource.Steam);

        Assert.NotNull(result);
        Assert.Equal(shipping, result.ExecutablePath);
        Assert.Equal(Path.GetDirectoryName(shipping), result.DeploymentDirectory);
        Assert.Contains("DLSS", result.DetectedTechnologies);
        Assert.Equal(GameSource.Steam, result.Source);
    }

    [Fact]
    public async Task ScanAsync_ReturnsNullWhenNoExecutableExists()
    {
        using var temporary = new TemporaryDirectory();
        temporary.WriteFile("nvngx_dlss.dll");

        var result = await new GameDetector().ScanAsync(temporary.Path);

        Assert.Null(result);
    }
}

