using IranDirect.Core;
using IranDirect.Core.Ipc;
using IranDirect.Core.ServiceLifecycle;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace IranDirect.Tray;

public sealed class TrayApplicationContext :
    ApplicationContext
{
    private const int RunningPollIntervalMs = 15000;
    private const int TransitionalPollIntervalMs = 2000;

    private readonly IranDirectServiceClient _client;
    private readonly IServiceLifecycle _serviceLifecycle;
    private readonly bool _isAdministrator;

    private readonly NotifyIcon _notifyIcon;

    private readonly ToolStripMenuItem _serviceStatusItem;
    private readonly ToolStripMenuItem _startServiceItem;
    private readonly ToolStripMenuItem _stopServiceItem;
    private readonly ToolStripMenuItem _restartServiceItem;
    private readonly ToolStripMenuItem _installServiceItem;
    private readonly ToolStripMenuItem _uninstallServiceItem;

    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _enableItem;
    private readonly ToolStripMenuItem _disableItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _repairItem;
    private readonly ToolStripMenuItem _configItem;
    private readonly ToolStripMenuItem _customRoutesItem;
    private readonly ToolStripMenuItem _logsItem;

    private readonly System.Windows.Forms.Timer _timer;

    private bool _busy;

    public TrayApplicationContext()
    {
        _client = new IranDirectServiceClient();

        string binaryPath = Path.Combine(
            AppContext.BaseDirectory,
            "IranDirect.Service.exe");

        _serviceLifecycle = new WindowsServiceLifecycle(
            new WindowsServiceControllerAdapter(
                IranDirectServiceNames.ServiceName),
            IranDirectServiceNames.ServiceName,
            File.Exists(binaryPath) ? binaryPath : null);

        _isAdministrator = CheckIsAdministrator();

        _serviceStatusItem = new ToolStripMenuItem(
            "Service: Checking...")
        {
            Enabled = false
        };

        _startServiceItem = new ToolStripMenuItem(
            "Start Service");
        _stopServiceItem = new ToolStripMenuItem(
            "Stop Service");
        _restartServiceItem = new ToolStripMenuItem(
            "Restart Service");
        _installServiceItem = new ToolStripMenuItem(
            "Install Service");
        _uninstallServiceItem = new ToolStripMenuItem(
            "Uninstall Service");

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
        _configItem = new ToolStripMenuItem(
            "View configuration");
        _customRoutesItem =
            CustomRouteMenuFactory.CreateCustomRoutesItem();
        _logsItem = new ToolStripMenuItem(
            "Open Event Viewer");

        ToolStripMenuItem exitItem = new("Exit");

        _startServiceItem.Click +=
            async (_, _) =>
                await ExecuteLifecycleActionAsync(
                    () => _serviceLifecycle.StartAsync(),
                    "start");

        _stopServiceItem.Click +=
            async (_, _) =>
                await ExecuteLifecycleActionAsync(
                    () => _serviceLifecycle.StopAsync(),
                    "stop");

        _restartServiceItem.Click +=
            async (_, _) =>
                await ExecuteLifecycleActionAsync(
                    () => _serviceLifecycle.RestartAsync(),
                    "restart");

        _installServiceItem.Click +=
            async (_, _) =>
                await ExecuteLifecycleActionAsync(
                    () => _serviceLifecycle.InstallAsync(),
                    "install");

        _uninstallServiceItem.Click +=
            async (_, _) =>
                await ExecuteLifecycleActionAsync(
                    () => _serviceLifecycle.UninstallAsync(),
                    "uninstall");

        _enableItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync(IranDirectCommand.Enable);

        _disableItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync(IranDirectCommand.Disable);

        _updateItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync(
                    IranDirectCommand.UpdatePrefixes);

        _repairItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync(IranDirectCommand.Repair);

        _configItem.Click +=
            async (_, _) => await ShowConfigurationAsync();

        _customRoutesItem.Click +=
            async (_, _) => await ShowCustomRoutesDialogAsync();

        _logsItem.Click += (_, _) => OpenEventViewer();

        exitItem.Click += (_, _) => ExitApplication();

        ContextMenuStrip menu = new();

        menu.Items.Add(_serviceStatusItem);
        menu.Items.Add(_startServiceItem);
        menu.Items.Add(_stopServiceItem);
        menu.Items.Add(_restartServiceItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_installServiceItem);
        menu.Items.Add(_uninstallServiceItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(_enableItem);
        menu.Items.Add(_disableItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(_repairItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_configItem);
        menu.Items.Add(_customRoutesItem);
        menu.Items.Add(_logsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "IranDirect",
            Icon = LoadApplicationIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick +=
            async (_, _) =>
                await RefreshStatusAsync(
                    showMessage: true);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = TransitionalPollIntervalMs
        };

        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();

        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        if (_busy)
            return;

        ServiceStatusSnapshot snapshot;

        try
        {
            snapshot = _serviceLifecycle.GetStatus();
        }
        catch
        {
            snapshot = ServiceStatusSnapshot.NotInstalled;
        }

        ApplyServiceStatus(snapshot);
        AdjustPolling(snapshot);

        if (snapshot.Running)
        {
            await RefreshStatusAsync(showMessage: false);
        }
        else
        {
            SetRuntimeUnavailable(snapshot);
        }
    }

    private void ApplyServiceStatus(
        ServiceStatusSnapshot snapshot)
    {
        string stateText = ServiceStateText(snapshot);

        _serviceStatusItem.Text =
            _isAdministrator
                ? $"Service: {stateText}"
                : $"Service: {stateText} (not elevated)";

        _notifyIcon.Text = $"IranDirect — Service {stateText}";

        bool busy = _busy;

        _installServiceItem.Enabled =
            !snapshot.Installed && !busy;
        _uninstallServiceItem.Enabled =
            snapshot.Installed && !busy;
        _startServiceItem.Enabled =
            snapshot.CanStart && !busy;
        _stopServiceItem.Enabled =
            snapshot.CanStop && !busy;
        _restartServiceItem.Enabled =
            snapshot.CanRestart && !busy;
    }

    private void AdjustPolling(
        ServiceStatusSnapshot snapshot)
    {
        int interval = snapshot.Running
            ? RunningPollIntervalMs
            : TransitionalPollIntervalMs;

        if (_timer.Interval != interval)
            _timer.Interval = interval;
    }

    private async Task ExecuteLifecycleActionAsync(
        Func<Task> action,
        string verb)
    {
        if (_busy)
            return;

        if (!_isAdministrator)
        {
            PromptElevation(verb);
            return;
        }

        SetBusy(true);

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                $"IranDirect — could not {verb} service",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await PollAsync();
        }
    }

    private void PromptElevation(string verb)
    {
        DialogResult result = MessageBox.Show(
            $"Administrator privileges are required to {verb} " +
            "the IranDirect service.\n\n" +
            "Restart the tray as administrator?",
            "IranDirect",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        if (RelaunchElevated())
            ExitApplication();
    }

    private static bool RelaunchElevated()
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = Environment.ProcessPath
                    ?? Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private async Task ExecuteCommandAsync(
        IranDirectCommand command)
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

            await PollAsync();
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
                await _client.SendAsync(IranDirectCommand.Status);

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
        _configItem.Enabled = !_busy;
        _customRoutesItem.Enabled =
            CustomRouteMenuPolicy.IsAvailable(
                serviceRunning: true,
                _busy);

        _notifyIcon.Text =
            status.Enabled
                ? "IranDirect — Enabled"
                : "IranDirect — Disabled";
    }

    private void SetRuntimeUnavailable(
        ServiceStatusSnapshot snapshot)
    {
        string reason = snapshot.Installed
            ? $"Service {ServiceStateText(snapshot).ToLowerInvariant()}"
            : "Service not installed";

        SetUnavailable(reason);
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
        _configItem.Enabled = false;
        _customRoutesItem.Enabled = false;

        if (!_notifyIcon.Text.StartsWith(
                "IranDirect — Service",
                StringComparison.Ordinal))
        {
            _notifyIcon.Text =
                "IranDirect — Service unavailable";
        }
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
        _configItem.Enabled = false;
        _customRoutesItem.Enabled = false;

        _startServiceItem.Enabled = false;
        _stopServiceItem.Enabled = false;
        _restartServiceItem.Enabled = false;
        _installServiceItem.Enabled = false;
        _uninstallServiceItem.Enabled = false;
    }

    private async Task ShowConfigurationAsync()
    {
        try
        {
            ServiceResponse response =
                await _client.SendAsync(
                    IranDirectCommand.GetConfiguration);

            if (!response.Success ||
                response.Configuration is null)
            {
                MessageBox.Show(
                    response.Message,
                    "IranDirect configuration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var c = response.Configuration;

            MessageBox.Show(
                $"Enabled: {c.Enabled}\n" +
                $"VPN provider: {c.VpnProvider}\n" +
                $"VPN profile: {c.VpnProfilePath}\n" +
                $"Auto repair: {c.AutoRepair}\n" +
                $"Repair interval: {c.RepairInterval}\n" +
                $"Auto update prefixes: {c.AutoUpdatePrefixes}\n" +
                $"Prefix update interval: {c.PrefixUpdateInterval}",
                "IranDirect configuration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "IranDirect service unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task ShowCustomRoutesDialogAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            using CustomRouteDialog dialog = new(_client);
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "IranDirect custom routes",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            await PollAsync();
        }
    }

    private void OpenEventViewer()
    {
        try
        {
            Process.Start(
                new ProcessStartInfo("eventvwr.msc")
                {
                    UseShellExecute = true
                });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "IranDirect",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string ServiceStateText(
        ServiceStatusSnapshot snapshot)
    {
        if (!snapshot.Installed)
            return "Not installed";

        return snapshot.Status switch
        {
            ServiceControllerStatus.Running => "Running",
            ServiceControllerStatus.Stopped => "Stopped",
            ServiceControllerStatus.StartPending => "Starting...",
            ServiceControllerStatus.StopPending => "Stopping...",
            ServiceControllerStatus.Paused => "Paused",
            ServiceControllerStatus.PausePending => "Pausing...",
            ServiceControllerStatus.ContinuePending => "Resuming...",
            _ => "Unknown"
        };
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

    private static bool CheckIsAdministrator()
    {
        using WindowsIdentity identity =
            WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(
            WindowsBuiltInRole.Administrator);
    }

    private static Icon LoadApplicationIcon()
    {
        using Stream stream =
            typeof(TrayApplicationContext).Assembly
                .GetManifestResourceStream(
                    "IranDirect.Tray.tray-icon.ico")
            ?? throw new InvalidOperationException(
                "Embedded tray icon resource is missing.");

        return new Icon(stream);
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
