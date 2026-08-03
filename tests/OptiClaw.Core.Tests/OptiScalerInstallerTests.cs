using OptiClaw.Core.Models;
using OptiClaw.Core.Services;

namespace OptiClaw.Core.Tests;

public sealed class OptiScalerInstallerTests
{
    [Fact]
    public async Task InstallAndRestore_PreservesExistingFilesExactly()
    {
        using var temporary = new TemporaryDirectory();
        var gameDirectory = temporary.CreateDirectory("Game");
        var executable = temporary.WriteFile(@"Game\Game.exe", [9, 9, 9]);
        var oldProxy = temporary.WriteFile(@"Game\dxgi.dll", [7, 7, 7, 7]);
        var payloadDirectory = temporary.CreateDirectory("Payload");
        temporary.WriteFile(@"Payload\OptiScaler.dll", [1, 2, 3, 4]);
        temporary.WriteFile(@"Payload\libxess.dll", [5, 6, 7, 8]);
        temporary.WriteFile(@"Payload\OptiScaler.ini", []);
        await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "OptiScaler.ini"), """
            [Upscalers]
            Dx11Upscaler=auto
            Dx12Upscaler=auto
            VulkanUpscaler=auto
            """);

        var game = new GameProfile
        {
            Name = "Game",
            InstallDirectory = gameDirectory,
            DeploymentDirectory = gameDirectory,
            ExecutablePath = executable
        };
        var appData = new AppDataPaths(temporary.CreateDirectory("AppData"));
        var installer = new OptiScalerInstaller(appData);

        var manifest = await installer.InstallAsync(
            game,
            new PreparedPayload("test-version", payloadDirectory),
            "dxgi.dll");

        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(oldProxy));
        Assert.True(File.Exists(Path.Combine(gameDirectory, "libxess.dll")));
        Assert.Contains("Dx12Upscaler=xess", await File.ReadAllTextAsync(Path.Combine(gameDirectory, "OptiScaler.ini")));
        Assert.True(File.Exists(Path.Combine(appData.GetInstallDirectory(game.Id, manifest.Id), "manifest.json")));

        var restore = await installer.RestoreAsync(game.Id, manifest.Id);

        Assert.True(restore.Succeeded);
        Assert.Equal([7, 7, 7, 7], await File.ReadAllBytesAsync(oldProxy));
        Assert.False(File.Exists(Path.Combine(gameDirectory, "libxess.dll")));
        Assert.False(File.Exists(Path.Combine(gameDirectory, "OptiScaler.ini")));
    }

    [Fact]
    public async Task Restore_RefusesToOverwriteAFileChangedAfterInstall()
    {
        using var temporary = new TemporaryDirectory();
        var gameDirectory = temporary.CreateDirectory("Game");
        var executable = temporary.WriteFile(@"Game\Game.exe");
        var payloadDirectory = temporary.CreateDirectory("Payload");
        temporary.WriteFile(@"Payload\OptiScaler.dll", [1]);
        temporary.WriteFile(@"Payload\libxess.dll", [2]);
        temporary.WriteFile(@"Payload\OptiScaler.ini", []);
        await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "OptiScaler.ini"), "[Upscalers]\nDx12Upscaler=auto\n");
        var game = new GameProfile
        {
            Name = "Game",
            InstallDirectory = gameDirectory,
            DeploymentDirectory = gameDirectory,
            ExecutablePath = executable
        };
        var appData = new AppDataPaths(temporary.CreateDirectory("AppData"));
        var installer = new OptiScalerInstaller(appData);
        var manifest = await installer.InstallAsync(game, new PreparedPayload("test", payloadDirectory), "dxgi.dll");
        await File.WriteAllBytesAsync(Path.Combine(gameDirectory, "dxgi.dll"), [99]);

        var result = await installer.RestoreAsync(game.Id, manifest.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("dxgi.dll", result.Conflicts);
        Assert.Equal([99], await File.ReadAllBytesAsync(Path.Combine(gameDirectory, "dxgi.dll")));
    }
}

