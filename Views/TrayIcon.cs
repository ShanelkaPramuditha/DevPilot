using System.Windows;
using DevPilot.Models;
using DevPilot.Services.Wsl;
using Forms = System.Windows.Forms;

namespace DevPilot.Views;

/// <summary>
/// DevPilot's icon in the notification area: open the window, open a terminal in a
/// distribution, start or stop distributions, and notifications for finished work.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(MainWindow window)
    {
        _window = window;

        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/DevPilot;component/Assets/DevPilot.ico"))!.Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize),
            Text = "DevPilot",
            ContextMenuStrip = new Forms.ContextMenuStrip(),
            Visible = true
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _window.ShowFromTray();
            }
        };
        _icon.BalloonTipClicked += (_, _) => _window.ShowFromTray();
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        BuildMenu();
    }

    /// <summary>Shows a notification next to the clock.</summary>
    public void Notify(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    /// <summary>The menu is rebuilt each time it opens, so it shows the distributions' current state.</summary>
    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();

        var open = new Forms.ToolStripMenuItem("Open DevPilot", null, (_, _) => _window.ShowFromTray())
        {
            Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold)
        };
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());

        foreach (var distro in ReadDistributions())
        {
            var item = new Forms.ToolStripMenuItem($"{distro.Name}  ({(distro.IsRunning ? "running" : "stopped")})");
            item.DropDownItems.Add("Open Terminal", null, (_, _) => Run(() =>
            {
                WslService.OpenTerminal(distro.Name, "--cd", "~");
                return Task.CompletedTask;
            }));
            if (distro.IsRunning)
            {
                item.DropDownItems.Add("Stop", null, (_, _) => Run(async () =>
                {
                    await WslService.RunCheckedAsync("--terminate", distro.Name);
                    Notify(distro.Name, $"{distro.Name} was stopped.");
                }));
            }
            else
            {
                item.DropDownItems.Add("Start", null, (_, _) => Run(async () =>
                {
                    await WslService.StartInBackgroundAsync(distro.Name);
                    Notify(distro.Name, $"{distro.Name} is running.");
                }));
            }

            menu.Items.Add(item);
        }

        if (menu.Items.Count > 2)
        {
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Shut Down WSL", null, (_, _) => Run(async () =>
            {
                await WslService.RunCheckedAsync("--shutdown");
                Notify("WSL", "All distributions were stopped.");
            }));
            menu.Items.Add(new Forms.ToolStripSeparator());
        }

        menu.Items.Add("Exit DevPilot", null, (_, _) => _window.ExitApplication());
    }

    /// <summary>Reads the distributions without blocking on the UI thread's context.</summary>
    private static List<WslDistro> ReadDistributions()
    {
        try
        {
            var listing = Task.Run(WslService.GetInstalledAsync);
            return listing.Wait(TimeSpan.FromSeconds(5)) ? listing.Result : [];
        }
        catch (AggregateException)
        {
            return [];
        }
    }

    private void Run(Func<Task> action)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await action();
                await _window.RefreshAfterTrayActionAsync();
            }
            catch (Exception ex)
            {
                Notify("DevPilot", ex.Message);
            }
        });
    }
}
