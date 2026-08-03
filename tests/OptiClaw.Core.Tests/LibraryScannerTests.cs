using OptiClaw.Core.Services;

namespace OptiClaw.Core.Tests;

public sealed class LibraryScannerTests
{
    [Fact]
    public async Task ScanFolderAsync_WithSeveralUnknownChildrenFallsBackToTheSelectedRoot()
    {
        using var temporary = new TemporaryDirectory();
        temporary.WriteFile(@"First\First.exe");
        temporary.WriteFile(@"Second\Second.exe");
        var scanner = new LibraryScanner(new GameDetector());

        var results = await scanner.ScanFolderAsync(temporary.Path);

        Assert.Single(results);
        Assert.StartsWith(temporary.Path, results[0].ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }
}
