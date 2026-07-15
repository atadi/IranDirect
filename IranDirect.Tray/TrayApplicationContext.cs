using IranDirect.Core;
using IranDirect.Core.Ipc;
using IranDirect.Tray.Ipc;

namespace IranDirect.Tray;

public sealed class TrayApplicationContext :
    ApplicationContext
{
    private readonly IranDirectServiceClient _client;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _enableItem;
    private readonly ToolStripMenuItem _disableItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _repairItem;
    private readonly System.Windows.Forms.Timer _timer;

    private bool _busy;

    public TrayApplicationContext()
    {
        _client = new IranDirectServiceClient();

        _statusItem = new ToolStripMenuItem(
            "Status: Checking...")
        {
            Enabled = false
        };

        _enableItem = new ToolStripMenuItem("Enable");
        _disableItem = new ToolStripMenuItem("Disable");
        _updateItem = new ToolStripMenuItem(
            "Update prefixes");
        _repairItem = new ToolStripMenuItem(
            "Repair routes");

        ToolStripMenuItem exitItem =
            new("Exit");

        _enableItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync("enable");

        _disableItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync("disable");

        _updateItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync("update");

        _repairItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync("repair");

        exitItem.Click +=
            (_, _) => ExitApplication();

        ContextMenuStrip menu = new();

        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enableItem);
        menu.Items.Add(_disableItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(_repairItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "IranDirect",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick +=
            async (_, _) =>
                await RefreshStatusAsync(
                    showMessage: true);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 15000
        };

        _timer.Tick +=
            async (_, _) =>
                await RefreshStatusAsync(
                    showMessage: false);

        _timer.Start();

        _ = RefreshStatusAsync(
            showMessage: false);
    }

    private async Task ExecuteCommandAsync(
        string command)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);

        try
        {
            ServiceResponse response =
                await _client.SendAsync(command);

            if (!response.Success)
            {
                MessageBox.Show(
                    response.Message,
                    "IranDirect",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else
            {
                _notifyIcon.ShowBalloonTip(
                    3000,
                    "IranDirect",
                    response.Message,
                    ToolTipIcon.Info);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "IranDirect service unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);

            await RefreshStatusAsync(
                showMessage: false);
        }
    }

    private async Task RefreshStatusAsync(
        bool showMessage)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            ServiceResponse response =
                await _client.SendAsync("status");

            if (!response.Success ||
                response.Status is null)
            {
                SetUnavailable(response.Message);
                return;
            }

            ApplyStatus(response.Status);

            if (showMessage)
            {
                MessageBox.Show(
                    BuildStatusText(response.Status),
                    "IranDirect status",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch
        {
            SetUnavailable("Service unavailable");
        }
    }

    private void ApplyStatus(
        IranDirectStatus status)
    {
        _statusItem.Text =
            status.Enabled
                ? $"Enabled — " +
                  $"{status.InstalledRouteCount}/" +
                  $"{status.PrefixCount} routes"
                : $"Disabled — " +
                  $"{status.InstalledRouteCount}/" +
                  $"{status.PrefixCount} routes";

        _enableItem.Enabled =
            !status.Enabled && !_busy;

        _disableItem.Enabled =
            status.Enabled && !_busy;

        _updateItem.Enabled = !_busy;
        _repairItem.Enabled =
            status.Enabled && !_busy;

        _notifyIcon.Text =
            status.Enabled
                ? "IranDirect — Enabled"
                : "IranDirect — Disabled";
    }

    private void SetUnavailable(
        string message)
    {
        _statusItem.Text =
            $"Unavailable — {message}";

        _enableItem.Enabled = false;
        _disableItem.Enabled = false;
        _updateItem.Enabled = false;
        _repairItem.Enabled = false;

        _notifyIcon.Text =
            "IranDirect — Service unavailable";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;

        _statusItem.Text =
            busy
                ? "Working..."
                : _statusItem.Text;

        _enableItem.Enabled = false;
        _disableItem.Enabled = false;
        _updateItem.Enabled = false;
        _repairItem.Enabled = false;
    }

    private static string BuildStatusText(
        IranDirectStatus status)
    {
        return
            $"Enabled: {status.Enabled}\n" +
            $"Gateway: {status.Gateway ?? "Unknown"}\n" +
            $"Interface: " +
            $"{status.InterfaceName ?? "Unknown"} " +
            $"({status.InterfaceIndex})\n" +
            $"Routes: " +
            $"{status.InstalledRouteCount}/" +
            $"{status.PrefixCount}\n" +
            $"Prefixes updated: " +
            $"{status.PrefixesUpdatedAt?.ToLocalTime()}";
    }

    private void ExitApplication()
    {
        _timer.Stop();
        _timer.Dispose();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        ExitThread();
    }
}
