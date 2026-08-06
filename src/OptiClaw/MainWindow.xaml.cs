using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OptiClaw.Core.Models;
using OptiClaw.Core.Services;
using Windows.Graphics;
using Windows.UI.ViewManagement;
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
    private const int PreferredWindowWidth = 1340;
    private const int PreferredWindowHeight = 960;
    private const int MinimumWindowWidth = 1008;
    private const int MinimumWindowHeight = 640;
    private const int WorkAreaMargin = 16;
    private const int MinimumStartupSurfaceDurationMs = 100;
    private const double StandardDpi = 96;

    private readonly AppDataPaths _paths = new();
    private readonly GameDetector _detector = new();
    private readonly ProfileStore _profileStore;
    private readonly SettingsStore _settingsStore;
    private readonly LibraryScanner _libraryScanner;
    private readonly OptiScalerInstaller _installer;
    private readonly OptiScalerReleaseClient _releaseClient;
    private bool _isBusy;
    private bool _sortAscending = true;
    private AppSettings _settings = new();
    private Guid? _loadedFrameGenerationInstallId;
    private AppWindow? _appWindow;
    private nint _windowHandle;
    private nint _appIconHandle;
    private bool _hasInitialized;
    private bool _startupReady;
    private double? _lastRestoredWindowWidth;
    private double? _lastRestoredWindowHeight;
    private bool _isWindowPlacementReady;
    private bool _shouldRestoreMaximized;
    private long _startupStartedAt;
    private double _windowScale = 1;

    public MainWindow()
    {
        InitializeComponent();
        _profileStore = new ProfileStore(_paths);
        _settingsStore = new SettingsStore(_paths);
        _libraryScanner = new LibraryScanner(_detector);
        _installer = new OptiScalerInstaller(_paths);
        _releaseClient = new OptiScalerReleaseClient(_paths);
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            UpdateTitleBarColors();
        };
        LoadThemePreference();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindow();
        StartupRevealStoryboard.Completed += (_, _) =>
        {
            CompleteStartupReveal();
        };
        Closed += MainWindow_Closed;
    }

    public ObservableCollection<GameProfile> Games { get; } = [];
    public ObservableCollection<GameProfile> VisibleGames { get; } = [];
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

    private void GameSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        UpdateLibraryState();

    private void GameSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        GameSearchBox.IsSuggestionListOpen = false;
        SortGamesButton.Focus(FocusState.Programmatic);
    }

    private void SortGames_Click(object sender, RoutedEventArgs e)
    {
        _sortAscending = !_sortAscending;
        var accessibleLabel = _sortAscending ? "Sort games Z to A" : "Sort games A to Z";
        ToolTipService.SetToolTip(SortGamesButton, accessibleLabel);
        AutomationProperties.SetName(SortGamesButton, accessibleLabel);
        UpdateLibraryState();
    }

    private void ThemeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is not string themeName
            || !Enum.TryParse(themeName, out ElementTheme theme))
        {
            return;
        }

        if (RootGrid.RequestedTheme != theme)
        {
            ApplyTheme(theme);
            SaveThemePreference(theme);
        }
    }

    private async void Upstream_Click(object sender, RoutedEventArgs e)
    {
        var opened = await Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/optiscaler/OptiScaler"));
        if (!opened)
        {
            StatusText.Text = "Could not open the OptiScaler website";
        }
    }

    private void LoadThemePreference()
    {
        var theme = ElementTheme.Default;
        try
        {
            _settings = _settingsStore.Load();
            if (!Enum.TryParse(_settings.Theme, out theme))
            {
                theme = ElementTheme.Default;
            }
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"Could not load the saved theme: {exception}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine($"Could not load the saved theme: {exception}");
        }

        ApplyTheme(theme);
    }

    private void ApplyTheme(ElementTheme theme)
    {
        RootGrid.RequestedTheme = theme;
        ThemeSelector.SelectedItem = theme switch
        {
            ElementTheme.Light => LightThemeItem,
            ElementTheme.Dark => DarkThemeItem,
            _ => SystemThemeItem
        };
        UpdateTitleBarColors();
    }

    private void SaveThemePreference(ElementTheme theme)
    {
        try
        {
            _settings.Theme = theme.ToString();
            _settingsStore.Save(_settings);
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"Could not save the theme preference: {exception}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine($"Could not save the theme preference: {exception}");
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        try
        {
            _settings.WindowMaximized = _shouldRestoreMaximized;
            if (_lastRestoredWindowWidth is double width
                && _lastRestoredWindowHeight is double height)
            {
                _settings.WindowWidth = Math.Round(width, 2);
                _settings.WindowHeight = Math.Round(height, 2);
            }

            _settingsStore.Save(_settings);
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"Could not save the window size: {exception}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine($"Could not save the window size: {exception}");
        }
        finally
        {
            ReleaseWindowIcon();
            _releaseClient.Dispose();
        }
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasInitialized)
        {
            return;
        }

        _hasInitialized = true;
        RootGrid.XamlRoot.Changed += XamlRoot_Changed;
        UpdateWindowScale(RootGrid.XamlRoot.RasterizationScale);
        _startupStartedAt = Stopwatch.GetTimestamp();
        _ = ShowStartupIndicatorAfterDelayAsync();
        Exception? loadException = null;
        _isBusy = true;
        StatusText.Text = "Loading game library…";

        try
        {
            var savedGames = await _profileStore.LoadAsync();
            foreach (var game in savedGames)
            {
                Games.Add(game);
            }
        }
        catch (Exception exception)
        {
            loadException = exception;
        }
        finally
        {
            _startupReady = true;
            _isBusy = false;
            StatusText.Text = loadException is null ? "Ready" : "Library could not be loaded";
            UpdateLibraryState();
            await StabilizeStartupSurfaceAsync();
            RevealApplication();
        }

        if (loadException is not null)
        {
            await ShowMessageAsync("OptiClaw couldn't load your library", GetFriendlyError(loadException));
        }

        _ = CheckLatestReleaseAsync();
    }

    private async Task StabilizeStartupSurfaceAsync()
    {
        var elapsed = Stopwatch.GetElapsedTime(_startupStartedAt);
        var remaining = TimeSpan.FromMilliseconds(MinimumStartupSurfaceDurationMs) - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }

        await WaitForNextCompositionFrameAsync();
    }

    private static async Task WaitForNextCompositionFrameAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            completion.TrySetResult(true);
        };

        CompositionTarget.Rendering += handler;
        await Task.WhenAny(completion.Task, Task.Delay(100));
        CompositionTarget.Rendering -= handler;
    }

    private async Task ShowStartupIndicatorAfterDelayAsync()
    {
        await Task.Delay(250);
        if (_startupReady || StartupOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        var animationsEnabled = true;
        try
        {
            animationsEnabled = new UISettings().AnimationsEnabled;
        }
        catch
        {
            // Fall back to the short fade if Windows cannot provide the preference.
        }

        if (animationsEnabled)
        {
            StartupIndicatorStoryboard.Begin();
        }
        else
        {
            StartupIndicator.Opacity = 1;
        }
    }

    private void RevealApplication()
    {
        var animationsEnabled = true;
        try
        {
            animationsEnabled = new UISettings().AnimationsEnabled;
        }
        catch
        {
            // Fall back to the short reveal if Windows cannot provide the preference.
        }

        if (animationsEnabled)
        {
            StartupRevealStoryboard.Begin();
            return;
        }

        CompleteStartupReveal();
    }

    private void CompleteStartupReveal()
    {
        // Commit the final values before stopping the storyboards so they no longer
        // retain composition state for the large app surfaces after startup.
        StartupOverlay.Opacity = 0;
        HeaderPanel.Opacity = 1;
        WorkspacePanel.Opacity = 1;
        FooterPanel.Opacity = 1;
        StartupRevealStoryboard.Stop();
        StartupIndicatorStoryboard.Stop();
        StartupOverlay.Visibility = Visibility.Collapsed;
        StartupOverlay.IsHitTestVisible = false;
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id)
        {
            Title = "Add a game executable",
            CommitButtonText = "Add game",
            InitialFileTypeIndex = 0,
            ViewMode = Microsoft.Windows.Storage.Pickers.PickerViewMode.List
        };
        picker.FileTypeChoices.Add("Executable files (*.exe)", new List<string> { ".exe" });
        picker.FileTypeChoices.Add("All files (*.*)", new List<string> { "*" });
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var executablePath = file.Path;
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        var root = FindLikelyInstallRoot(executablePath);
        await RunBusyAsync($"Scanning {executableName}…", async () =>
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
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id)
        {
            Title = "Choose a game folder",
            CommitButtonText = "Scan folder",
            SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
            ViewMode = Microsoft.Windows.Storage.Pickers.PickerViewMode.List
        };
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        var folderPath = folder.Path;
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        var progress = new Progress<string>(message => StatusText.Text = message);
        await RunBusyAsync($"Scanning {folderName}…", async () =>
        {
            var results = await _libraryScanner.ScanFolderAsync(folderPath, progress);
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
            UpstreamButton.Label = $"OptiScaler {payload.Version}";
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
        GamesList.SelectedIndex = VisibleGames.Count > 0 ? 0 : -1;
    }

    private async Task CheckLatestReleaseAsync()
    {
        try
        {
            var release = await _releaseClient.GetLatestReleaseAsync();
            UpstreamButton.Label = $"OptiScaler {release.Version}";
        }
        catch
        {
            UpstreamButton.Label = "Offline";
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
        if (VisibleGames.Contains(existing))
        {
            GamesList.SelectedItem = existing;
        }
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
        RefreshVisibleGames();
        GameCountText.Text = $"GAME LIBRARY - {Games.Count} GAME{(Games.Count == 1 ? string.Empty : "S")}";
        var hasNoVisibleGames = VisibleGames.Count == 0;
        EmptyLibraryPanel.Visibility = hasNoVisibleGames ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryTitle.Text = Games.Count == 0 ? "No compatible games yet" : "No matching games";
        EmptyLibraryHelpText.Text = Games.Count == 0
            ? "Add an EXE or scan a folder."
            : "Try a different search.";
        UpdateSelectionState();
    }

    private void RefreshVisibleGames()
    {
        var selectedGame = SelectedGame;
        var query = GameSearchBox.Text.Trim();
        var matchingGames = Games.Where(game =>
            query.Length == 0 || game.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        matchingGames = _sortAscending
            ? matchingGames.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            : matchingGames.OrderByDescending(game => game.Name, StringComparer.CurrentCultureIgnoreCase);

        VisibleGames.Clear();
        foreach (var game in matchingGames)
        {
            VisibleGames.Add(game);
        }

        if (selectedGame is not null && VisibleGames.Contains(selectedGame))
        {
            GamesList.SelectedItem = selectedGame;
        }
        else
        {
            GamesList.SelectedIndex = VisibleGames.Count > 0 ? 0 : -1;
        }
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
            game.IsInstalled ? "ClawXeSSBlueBrush" : "ClawGreenBrush"];
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
        _windowHandle = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;
        ConfigureWindowIcon();
        _windowScale = GetDpiForWindow(_windowHandle) / StandardDpi;
        var windowId = _appWindow.Id;

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        if (displayArea is not null)
        {
            PlaceWindowOnDisplay(displayArea);
            ApplyWindowSizeConstraints(displayArea);
        }

        RememberRestoredWindowSize(_appWindow);
        _shouldRestoreMaximized = _settings.WindowMaximized;
        if (_shouldRestoreMaximized && _appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        _isWindowPlacementReady = true;

        _appWindow.Changed += AppWindow_Changed;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            UpdateTitleBarColors();
        }
    }

    private void ConfigureWindowIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (_appWindow is null || string.IsNullOrEmpty(executablePath))
        {
            return;
        }

        nint largeIcon = 0;
        nint smallIcon = 0;
        try
        {
            if (ExtractIconEx(executablePath, 0, out largeIcon, out smallIcon, 1) == 0)
            {
                return;
            }

            var selectedIcon = largeIcon != 0 ? largeIcon : smallIcon;
            if (selectedIcon == 0)
            {
                return;
            }

            _appWindow.SetIcon(Win32Interop.GetIconIdFromIcon(selectedIcon));
            _appIconHandle = selectedIcon;
            if (selectedIcon == largeIcon)
            {
                largeIcon = 0;
            }
            else
            {
                smallIcon = 0;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not set the window icon: {exception}");
        }
        finally
        {
            if (largeIcon != 0)
            {
                DestroyIcon(largeIcon);
            }

            if (smallIcon != 0)
            {
                DestroyIcon(smallIcon);
            }
        }
    }

    private void ReleaseWindowIcon()
    {
        if (_appIconHandle == 0)
        {
            return;
        }

        DestroyIcon(_appIconHandle);
        _appIconHandle = 0;
    }

    private void PlaceWindowOnDisplay(DisplayArea displayArea)
    {
        if (_appWindow is null)
        {
            return;
        }

        var displayScale = _windowScale;
        var margin = (int)Math.Round(WorkAreaMargin * displayScale);
        var workArea = displayArea.WorkArea;
        var availableWidth = Math.Max(1, workArea.Width - (margin * 2));
        var availableHeight = Math.Max(1, workArea.Height - (margin * 2));
        var preferredWidth = GetPreferredWindowDimension(
            _settings.WindowWidth,
            PreferredWindowWidth,
            MinimumWindowWidth);
        var preferredHeight = GetPreferredWindowDimension(
            _settings.WindowHeight,
            PreferredWindowHeight,
            MinimumWindowHeight);
        var width = Math.Min((int)Math.Round(preferredWidth * displayScale), availableWidth);
        var height = Math.Min((int)Math.Round(preferredHeight * displayScale), availableHeight);
        var bounds = new RectInt32(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2),
            width,
            height);
        _appWindow.MoveAndResize(bounds, displayArea);
    }

    private void ApplyWindowSizeConstraints(DisplayArea displayArea)
    {
        if (_appWindow?.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        presenter.PreferredMinimumWidth = Math.Min(
            (int)Math.Round(MinimumWindowWidth * _windowScale),
            workArea.Width);
        presenter.PreferredMinimumHeight = Math.Min(
            (int)Math.Round(MinimumWindowHeight * _windowScale),
            workArea.Height);
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        RememberWindowPresentationState(sender);

        if (args.DidSizeChange && _isWindowPlacementReady)
        {
            RememberRestoredWindowSize(sender);
        }

    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) =>
        UpdateWindowScale(sender.RasterizationScale);

    private void UpdateWindowScale(double scale)
    {
        if (_appWindow is null || !double.IsFinite(scale) || scale <= 0
            || Math.Abs(scale - _windowScale) < 0.001)
        {
            return;
        }

        _windowScale = scale;
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
        if (displayArea is not null)
        {
            ApplyWindowSizeConstraints(displayArea);
        }
    }

    private void RememberWindowPresentationState(AppWindow appWindow)
    {
        if (appWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            _shouldRestoreMaximized = true;
        }
        else if (presenter.State == OverlappedPresenterState.Restored)
        {
            _shouldRestoreMaximized = false;
        }
    }

    private void RememberRestoredWindowSize(AppWindow appWindow)
    {
        if (appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return;
        }

        _lastRestoredWindowWidth = appWindow.Size.Width / _windowScale;
        _lastRestoredWindowHeight = appWindow.Size.Height / _windowScale;
    }

    private static double GetPreferredWindowDimension(double? savedValue, double fallback, double minimum)
    {
        if (savedValue is not double value || !double.IsFinite(value) || value <= 0)
        {
            return fallback;
        }

        return Math.Max(value, minimum);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string fileName,
        int iconIndex,
        out nint largeIcon,
        out nint smallIcon,
        uint iconCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);

    private void UpdateTitleBarColors()
    {
        if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var foreground = RootGrid.ActualTheme == ElementTheme.Light ? Colors.Black : Colors.White;
        var inactiveForeground = RootGrid.ActualTheme == ElementTheme.Light ? Colors.DimGray : Colors.LightGray;
        _appWindow.TitleBar.ButtonForegroundColor = foreground;
        _appWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        _appWindow.TitleBar.ButtonPressedForegroundColor = foreground;
        _appWindow.TitleBar.ButtonInactiveForegroundColor = inactiveForeground;
    }
}
