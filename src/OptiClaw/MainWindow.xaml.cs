using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptiClaw.Core.Models;
using OptiClaw.Core.Services;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OptiClaw;

public sealed class FrameGenerationOption
{
    public FrameGenerationOption()
    {
    }

    public FrameGenerationOption(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed partial class MainWindow : Window
{
    private readonly AppDataPaths _paths = new();
    private readonly GameDetector _detector = new();
    private readonly ProfileStore _profileStore;
    private readonly LibraryScanner _libraryScanner;
    private readonly OptiScalerInstaller _installer;
    private readonly OptiScalerReleaseClient _releaseClient;
    private bool _isBusy;
    private Guid? _loadedFrameGenerationInstallId;

    public MainWindow()
    {
        InitializeComponent();
        _profileStore = new ProfileStore(_paths);
        _libraryScanner = new LibraryScanner(_detector);
        _installer = new OptiScalerInstaller(_paths);
        _releaseClient = new OptiScalerReleaseClient(_paths);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindow();
        Closed += (_, _) => _releaseClient.Dispose();
    }

    public ObservableCollection<GameProfile> Games { get; } = [];
    public IReadOnlyList<string> ProxyDllNames => OptiScalerInstaller.SupportedProxyDllNames;
    public IReadOnlyList<FrameGenerationOption> FrameGenerationInputOptions { get; } =
    [
        new("DLSSG via Streamline — requires native in-game DLSS FG", "dlssg"),
        new("FSR 3.1 FG — requires native in-game FSR FG", "fsrfg"),
        new("OptiFG (Upscaler fallback)", "upscaler")
    ];
    public IReadOnlyList<FrameGenerationOption> FrameGenerationOutputOptions { get; } =
    [
        new("Intel XeFG", "xefg")
    ];
    public IReadOnlyList<FrameGenerationOption> FrameGenerationMultiplierOptions { get; } =
    [
        new("2x", "1"),
        new("3x", "2"),
        new("4x", "3")
    ];

    private GameProfile? SelectedGame => GamesList.SelectedItem as GameProfile;

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Loading game library…", async () =>
        {
            foreach (var game in await _profileStore.LoadAsync())
            {
                Games.Add(game);
            }
        });
        UpdateLibraryState();
        GamesList.SelectedIndex = Games.Count > 0 ? 0 : -1;

        _ = CheckLatestReleaseAsync();
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var executablePath = file.Path;
        var root = FindLikelyInstallRoot(executablePath);
        await RunBusyAsync($"Scanning {file.DisplayName}…", async () =>
        {
            var result = await _detector.ScanAsync(root, executablePath);
            if (result is null)
            {
                throw new InvalidOperationException("No usable game executable was found in that folder.");
            }

            AddOrUpdate(result);
            await _profileStore.SaveAsync(Games);
        });
    }

    private async void ScanLibraries_Click(object sender, RoutedEventArgs e)
    {
        var progress = new Progress<string>(message => StatusText.Text = message);
        await RunBusyAsync("Finding Steam and Xbox games…", async () =>
        {
            var results = await _libraryScanner.ScanInstalledLibrariesAsync(progress);
            foreach (var result in results)
            {
                AddOrUpdate(result);
            }

            await _profileStore.SaveAsync(Games);
            StatusText.Text = results.Count == 0
                ? "No new DLSS, FSR, or XeSS games found"
                : $"Found {results.Count} compatible game{(results.Count == 1 ? string.Empty : "s")}";
        }, keepCompletionStatus: true);
    }

    private async void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        var progress = new Progress<string>(message => StatusText.Text = message);
        await RunBusyAsync($"Scanning {folder.Name}…", async () =>
        {
            var results = await _libraryScanner.ScanFolderAsync(folder.Path, progress);
            foreach (var result in results)
            {
                AddOrUpdate(result);
            }

            await _profileStore.SaveAsync(Games);
            StatusText.Text = $"Added {results.Count} compatible game{(results.Count == 1 ? string.Empty : "s")}";
        }, keepCompletionStatus: true);
    }

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionState();

    private async void RescanGame_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        if (game is null)
        {
            return;
        }

        await RunBusyAsync($"Scanning {game.Name}…", async () =>
        {
            var result = await _detector.ScanAsync(
                game.InstallDirectory,
                game.ExecutablePath,
                game.Name,
                game.Source);
            if (result is null)
            {
                throw new InvalidOperationException("The game executable could not be found. It may have moved.");
            }

            ApplyDetection(game, result);
            await _profileStore.SaveAsync(Games);
        });
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        var proxyDllName = ProxyDllCombo.SelectedItem as string;
        if (game is null || proxyDllName is null || game.IsInstalled)
        {
            return;
        }

        var proxyPath = Path.Combine(game.DeploymentDirectory, proxyDllName);
        var conflictMessage = File.Exists(proxyPath)
            ? $"\n\n{proxyDllName} already exists. OptiClaw will back it up byte-for-byte before replacing it."
            : string.Empty;
        var confirmed = await ShowConfirmationAsync(
            "Install OptiScaler + XeSS?",
            $"Official OptiScaler files will be installed beside:\n{game.ExecutablePath}{conflictMessage}\n\nYou can restore every replaced file from this screen.",
            "Install");
        if (!confirmed)
        {
            return;
        }

        WorkProgress.IsIndeterminate = false;
        WorkProgress.Value = 0;
        var progress = new Progress<double>(value => WorkProgress.Value = value);
        await RunBusyAsync("Downloading and verifying official OptiScaler release…", async () =>
        {
            var payload = await _releaseClient.PrepareLatestAsync(progress);
            StatusText.Text = $"Installing OptiScaler {payload.Version}…";
            var manifest = await _installer.InstallAsync(game, payload, proxyDllName);
            game.ActiveInstallId = manifest.Id;
            game.InstalledVersion = manifest.OptiScalerVersion;
            await _profileStore.SaveAsync(Games);
            ReleaseVersionText.Text = $"OptiScaler {payload.Version}";
            StatusText.Text = $"XeSS enabled for {game.Name}";
        }, showProgress: true, keepCompletionStatus: true);
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        if (game?.ActiveInstallId is not Guid installId)
        {
            return;
        }

        if (!await ShowConfirmationAsync(
                "Restore original game files?",
                "OptiClaw will remove its installed files and put every previous DLL/config file back exactly as it was.",
                "Restore"))
        {
            return;
        }

        await RunBusyAsync($"Restoring {game.Name}…", async () =>
        {
            var result = await _installer.RestoreAsync(game.Id, installId);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "These installed files changed after setup, so OptiClaw left everything untouched:\n"
                    + string.Join("\n", result.Conflicts));
            }

            game.ActiveInstallId = null;
            game.InstalledVersion = null;
            await _profileStore.SaveAsync(Games);
            StatusText.Text = $"Original files restored for {game.Name}";
        }, keepCompletionStatus: true);
    }

    private async void ApplyFrameGeneration_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        if (game?.ActiveInstallId is not Guid installId
            || FrameGenerationInputCombo.SelectedItem is not FrameGenerationOption input
            || FrameGenerationOutputCombo.SelectedItem is not FrameGenerationOption output
            || FrameGenerationMultiplierCombo.SelectedItem is not FrameGenerationOption multiplier
            || !int.TryParse(multiplier.Value, out var interpolationCount))
        {
            return;
        }

        var settings = new FrameGenerationSettings(
            FrameGenerationEnabledToggle.IsOn,
            input.Value,
            output.Value,
            interpolationCount);
        await RunBusyAsync("Saving frame-generation settings…", async () =>
        {
            await _installer.UpdateFrameGenerationSettingsAsync(game.Id, installId, settings);
            _loadedFrameGenerationInstallId = installId;
            FrameGenerationHelpText.Text = settings.Enabled
                ? "Saved. Fully restart the game; XeFG requires Borderless display mode."
                : "Frame generation is disabled. Fully restart the game to apply the change.";
            StatusText.Text = $"Frame-generation settings saved for {game.Name}";
        }, keepCompletionStatus: true);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = SelectedGame?.DeploymentDirectory;
        if (directory is not null && Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
    }

    private async void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        if (game is null)
        {
            return;
        }

        if (game.IsInstalled)
        {
            await ShowMessageAsync("Restore first", "Restore this game's original files before removing it from OptiClaw.");
            return;
        }

        if (!await ShowConfirmationAsync("Remove game?", $"Remove {game.Name} from this library? No game files will be changed.", "Remove"))
        {
            return;
        }

        Games.Remove(game);
        await _profileStore.SaveAsync(Games);
        UpdateLibraryState();
        GamesList.SelectedIndex = Games.Count > 0 ? 0 : -1;
    }

    private async Task CheckLatestReleaseAsync()
    {
        try
        {
            var release = await _releaseClient.GetLatestReleaseAsync();
            ReleaseVersionText.Text = $"OptiScaler {release.Version}";
        }
        catch
        {
            ReleaseVersionText.Text = "Offline";
        }
    }

    private void AddOrUpdate(GameDetectionResult result)
    {
        var existing = Games.FirstOrDefault(game =>
            string.Equals(game.InstallDirectory, result.InstallDirectory, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = result.ToProfile();
            Games.Add(existing);
        }
        else
        {
            ApplyDetection(existing, result);
        }

        UpdateLibraryState();
        GamesList.SelectedItem = existing;
    }

    private static void ApplyDetection(GameProfile game, GameDetectionResult result)
    {
        game.Name = result.Name;
        game.InstallDirectory = result.InstallDirectory;
        game.ExecutablePath = result.ExecutablePath;
        game.DeploymentDirectory = result.DeploymentDirectory;
        game.DetectedTechnologies = [.. result.DetectedTechnologies];
        game.Source = result.Source;
        game.LastScannedAt = DateTimeOffset.UtcNow;
    }

    private void UpdateLibraryState()
    {
        GameCountText.Text = $"{Games.Count} game{(Games.Count == 1 ? string.Empty : "s")}";
        EmptyLibraryPanel.Visibility = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var game = SelectedGame;
        EmptyDetailsPanel.Visibility = game is null ? Visibility.Visible : Visibility.Collapsed;
        DetailsPanel.Visibility = game is null ? Visibility.Collapsed : Visibility.Visible;
        if (game is null)
        {
            _loadedFrameGenerationInstallId = null;
            return;
        }

        SelectedGameNameText.Text = game.Name;
        SelectedGameSourceText.Text = $"{game.Source} library";
        DetectedInputsText.Text = game.TechnologySummary;
        ExecutablePathText.Text = game.ExecutablePath;
        DeploymentPathText.Text = game.DeploymentDirectory;
        InstallStateText.Text = game.IsInstalled ? "INSTALLED" : "READY";
        InstallStateText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            game.IsInstalled ? "ClawXeSSBlueBrush" : "ClawOrangeBrush"];
        InstallStateBadge.Background = game.IsInstalled
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ClawXeSSBlueBadgeBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ClawReadyBadgeBrush"];
        FrameGenerationPanel.IsEnabled = !_isBusy && game.IsInstalled;
        FrameGenerationHelpText.Text = !game.IsInstalled
            ? "Install XeSS to configure frame generation."
            : "Use OptiFG when the game has no native FG. Native inputs require enabling FG in the game's settings. Changes require a full restart; XeFG requires Borderless display mode.";
        if (game.ActiveInstallId is Guid installId)
        {
            if (_loadedFrameGenerationInstallId != installId)
            {
                _loadedFrameGenerationInstallId = installId;
                _ = LoadFrameGenerationSettingsAsync(game, installId);
            }
        }
        else
        {
            _loadedFrameGenerationInstallId = null;
            ResetFrameGenerationControls();
        }
        InstallButton.IsEnabled = !_isBusy && !game.IsInstalled;
        RestoreButton.IsEnabled = !_isBusy && game.IsInstalled;
        ProxyDllCombo.IsEnabled = !_isBusy && !game.IsInstalled;
    }

    private async Task LoadFrameGenerationSettingsAsync(GameProfile game, Guid installId)
    {
        try
        {
            var settings = await _installer.LoadFrameGenerationSettingsAsync(game.Id, installId);
            if (!ReferenceEquals(SelectedGame, game) || game.ActiveInstallId != installId)
            {
                return;
            }

            FrameGenerationEnabledToggle.IsOn = settings.Enabled;
            SelectFrameGenerationOption(FrameGenerationInputCombo, FrameGenerationInputOptions, settings.Input);
            SelectFrameGenerationOption(FrameGenerationOutputCombo, FrameGenerationOutputOptions, settings.Output);
            SelectFrameGenerationOption(
                FrameGenerationMultiplierCombo,
                FrameGenerationMultiplierOptions,
                settings.InterpolationCount.ToString());
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(SelectedGame, game) && game.ActiveInstallId == installId)
            {
                _loadedFrameGenerationInstallId = null;
                FrameGenerationHelpText.Text = $"Could not read FG settings: {GetFriendlyError(exception)}";
            }
        }
    }

    private void ResetFrameGenerationControls()
    {
        FrameGenerationEnabledToggle.IsOn = false;
        FrameGenerationInputCombo.SelectedIndex = 0;
        FrameGenerationOutputCombo.SelectedIndex = 0;
        FrameGenerationMultiplierCombo.SelectedIndex = 0;
    }

    private static void SelectFrameGenerationOption(
        ComboBox comboBox,
        IEnumerable<FrameGenerationOption> options,
        string value)
    {
        comboBox.SelectedItem = options.FirstOrDefault(option =>
            option.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (comboBox.SelectedIndex < 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private async Task RunBusyAsync(
        string status,
        Func<Task> action,
        bool showProgress = false,
        bool keepCompletionStatus = false)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true, status, showProgress);
        try
        {
            await action();
            if (!keepCompletionStatus)
            {
                StatusText.Text = "Ready";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "Action failed";
            await ShowMessageAsync("OptiClaw couldn't finish", GetFriendlyError(exception));
        }
        finally
        {
            SetBusy(false, StatusText.Text, false);
            UpdateLibraryState();
        }
    }

    private void SetBusy(bool busy, string status, bool showProgress)
    {
        _isBusy = busy;
        StatusText.Text = status;
        BusyRing.IsActive = busy;
        WorkProgress.Visibility = busy && showProgress ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionState();
    }

    private async Task<bool> ShowConfirmationAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    private static string GetFriendlyError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Windows denied access to the game folder. Close the game and launcher, or run OptiClaw as administrator for protected installs.",
        HttpRequestException => "The official OptiScaler release could not be downloaded. Check your connection and try again.",
        _ => exception.Message
    };

    private static string FindLikelyInstallRoot(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        var marker = $"{Path.DirectorySeparatorChar}steamapps{Path.DirectorySeparatorChar}common{Path.DirectorySeparatorChar}";
        var markerIndex = fullPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var afterMarker = fullPath[(markerIndex + marker.Length)..];
            var firstSeparator = afterMarker.IndexOf(Path.DirectorySeparatorChar);
            if (firstSeparator > 0)
            {
                return fullPath[..(markerIndex + marker.Length + firstSeparator)];
            }
        }

        var executableDirectory = new DirectoryInfo(Path.GetDirectoryName(fullPath)!);
        if (executableDirectory.Name.Equals("Win64", StringComparison.OrdinalIgnoreCase)
            || executableDirectory.Name.Equals("WinGDK", StringComparison.OrdinalIgnoreCase))
        {
            var binaries = executableDirectory.Parent;
            var project = binaries?.Name.Equals("Binaries", StringComparison.OrdinalIgnoreCase) == true
                ? binaries.Parent
                : null;
            return project?.Parent?.FullName ?? project?.FullName ?? executableDirectory.FullName;
        }

        if (executableDirectory.Name.Equals("x64", StringComparison.OrdinalIgnoreCase)
            && executableDirectory.Parent?.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) == true)
        {
            return executableDirectory.Parent.Parent?.FullName ?? executableDirectory.FullName;
        }

        return executableDirectory.FullName;
    }

    private void ConfigureWindow()
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1180, 760));
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }
}
