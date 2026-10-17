using System.Diagnostics.CodeAnalysis;
using System.Windows;
using DevPilot.Services;
using DevPilot.Services.Platform;
using DevPilot.Theming;
using DevPilot.Views;

namespace DevPilot;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
[SuppressMessage("Reliability", "CA1001", Justification = "The single-instance handles live as long as the app and are disposed in OnExit.")]
public partial class App : Application
{
    private const string InstanceName = @"Local\DevPilot.SingleInstance";
    private const string ShowEventName = @"Local\DevPilot.ShowWindow";

    private Mutex? _instance;
    private EventWaitHandle? _showEvent;

    public static AppPreferences Preferences { get; } = AppPreferences.Load();

    protected override void OnStartup(StartupEventArgs e)
    {
        // One DevPilot per user: starting it again brings the running one to the front.
        _instance = new Mutex(initiallyOwned: true, InstanceName, out bool isFirst);
        if (!isFirst)
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The other instance is still starting or closing.
            }

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray());
            }
        })
        { IsBackground = true, Name = "DevPilot single instance" };
        listener.Start();

        // Apply the theme before the first window is created.
        ThemeManager.Apply(Preferences.Theme);
        base.OnStartup(e);

        var window = new MainWindow();
        bool startedWithWindows = e.Args.Contains(StartupRegistration.MinimizedSwitch, StringComparer.OrdinalIgnoreCase);
        var mode = startedWithWindows ? Preferences.EffectiveStartupWindow : StartupWindowMode.Window;
        if (mode == StartupWindowMode.NotificationArea)
        {
            // Stay in the notification area until opened, but still keep distributions
            // running and look for updates (a notification appears if there is one).
            window.StartInNotificationArea();
            _ = Views.MainWindow.StartKeptRunningDistrosAsync();
            _ = window.CheckForUpdatesWhileHiddenAsync();
            return;
        }

        window.Show();
        if (mode == StartupWindowMode.Taskbar)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
