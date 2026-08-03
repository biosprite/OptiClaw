using OptiClaw.Core.Models;

namespace OptiClaw.Core.Tests;

public sealed class GameProfileTests
{
    [Fact]
    public void StatusKind_ReflectsUnsupportedReadyAndInstalledStates()
    {
        var game = new GameProfile();

        Assert.Equal(GameStatusKind.Unsupported, game.StatusKind);

        game.DetectedTechnologies = ["DLSS"];
        Assert.Equal(GameStatusKind.Ready, game.StatusKind);

        game.ActiveInstallId = Guid.NewGuid();
        Assert.Equal(GameStatusKind.Installed, game.StatusKind);
    }

    [Fact]
    public void StatusKind_NotifiesWhenItsInputsChange()
    {
        var game = new GameProfile();
        var notifications = new List<string?>();
        game.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        game.DetectedTechnologies = ["FSR"];
        game.ActiveInstallId = Guid.NewGuid();

        Assert.Equal(2, notifications.Count(name => name == nameof(GameProfile.StatusKind)));
    }
}
