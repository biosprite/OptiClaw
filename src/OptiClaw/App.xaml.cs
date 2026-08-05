using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace OptiClaw;

public partial class App : Application
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly AppInstance _appInstance;
    private Window? _window;

    public App()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _appInstance = AppInstance.GetCurrent();
        _appInstance.Activated += AppInstance_Activated;
        UnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine(args.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private void AppInstance_Activated(object? sender, AppActivationArguments args)
    {
        _dispatcherQueue.TryEnqueue(ActivateMainWindow);
    }

    private void ActivateMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (_window.AppWindow.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Minimized
            } presenter)
        {
            presenter.Restore();
        }

        _window.Activate();
    }
}

