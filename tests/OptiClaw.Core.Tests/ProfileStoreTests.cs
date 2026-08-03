using OptiClaw.Core.Models;
using OptiClaw.Core.Services;

namespace OptiClaw.Core.Tests;

public sealed class ProfileStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsGameProfiles()
    {
        using var temporary = new TemporaryDirectory();
        var store = new ProfileStore(new AppDataPaths(temporary.Path));
        var id = Guid.NewGuid();
        var games = new[]
        {
            new GameProfile
            {
                Id = id,
                Name = "Example",
                ExecutablePath = @"C:\Games\Example\Example.exe",
                InstallDirectory = @"C:\Games\Example",
                DeploymentDirectory = @"C:\Games\Example",
                Source = GameSource.Custom,
                DetectedTechnologies = ["DLSS"]
            }
        };

        await store.SaveAsync(games);
        var loaded = await store.LoadAsync();

        var game = Assert.Single(loaded);
        Assert.Equal(id, game.Id);
        Assert.Equal("Example", game.Name);
        Assert.Equal(["DLSS"], game.DetectedTechnologies);
    }
}
