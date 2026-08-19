using PathVeer.Core;
using PathVeer.Core.Configuration;
using PathVeer.Core.Installer;
using PathVeer.Core.Ipc;
using PathVeer.Core.ServiceLifecycle;
using PathVeer.Core.Update;
using PathVeer.Core.Updates;
using PathVeer.Core.Prefixes;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace PathVeer.Tray;

public sealed class TrayApplicationContext :
    ApplicationContext
{
    private const int RunningPollIntervalMs = 15000;
    private const int TransitionalPollIntervalMs = 2000;

    private readonly PathVeerServiceClient _client;
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
    private readonly ToolStripMenuItem _prefixUpdateCheckItem;
    private readonly ToolStripMenuItem _appUpdateCheckItem;
    private readonly ToolStripMenuItem _repairItem;
    private readonly ToolStripMenuItem _configItem;
    private readonly ToolStripMenuItem _customRoutesItem;
    private readonly ToolStripMenuItem _runtimeSnapshotItem;
    private readonly ToolStripMenuItem _executionPreviewItem;
    private readonly ToolStripMenuItem _diagnosticsItem;
    private readonly ToolStripMenuItem _supportBundleItem;
    private readonly ToolStripMenuItem _logsItem;
    private readonly ToolStripMenuItem _countryItem;

    private readonly System.Windows.Forms.Timer _timer;

    // Cross-thread bridge so the "show existing" waiter thread can marshal UI
    // work (NotifyIcon is a UI control) onto the WinForms thread.
    private readonly Control _uiBridge = new Control();
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private Thread? _showWaiter;
    private Thread? _exitWaiter;
    private volatile bool _disposed;

    private readonly IPrefixUpdateNotificationTracker
        _prefixUpdateNotificationTracker;
    private readonly IPrefixUpdateNotificationSink
        _prefixUpdateNotificationSink;

    private bool _busy;

    public TrayApplicationContext()
    {
        _client = new PathVeerServiceClient();

        string binaryPath = Path.Combine(
            AppContext.BaseDirectory,
            "PathVeer.Service.exe");

        _serviceLifecycle = new WindowsServiceLifecycle(
            new WindowsServiceControllerAdapter(
                PathVeerServiceNames.ServiceName),
            PathVeerServiceNames.ServiceName,
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
        _prefixUpdateCheckItem = new ToolStripMenuItem(
            PrefixUpdateMenuPolicy.MenuItemText);
        _appUpdateCheckItem = new ToolStripMenuItem(
            "Check for app updates…");
        _repairItem = new ToolStripMenuItem(
            "Repair routes");
        _configItem = new ToolStripMenuItem(
            "View configuration");
        _customRoutesItem =
            CustomRouteMenuFactory.CreateCustomRoutesItem();
        _runtimeSnapshotItem = new ToolStripMenuItem(
            "View Runtime Snapshot...");
        _executionPreviewItem = new ToolStripMenuItem(
            ExecutionPreviewMenuPolicy.MenuItemText);
        _diagnosticsItem = new ToolStripMenuItem(
            "Run Diagnostics...");
        _supportBundleItem = new ToolStripMenuItem(
            SupportBundleMenuPolicy.MenuItemText);
        _logsItem = new ToolStripMenuItem(
            "Open Event Viewer");

        _countryItem = new ToolStripMenuItem(
            "Direct country: Iran (IR)");

        foreach (DirectCountryCode code in
                 DirectCountryCode.AllSupported)
        {
            ToolStripMenuItem item = new(
                $"{code.DisplayName} ({code.Code})")
            {
                Tag = code.Code
            };

            item.Click += async (_, _) =>
                await SetCountryAsync(code.Code);

            _countryItem.DropDownItems.Add(item);
        }

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
                await ExecuteCommandAsync(PathVeerCommand.Enable);

        _disableItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync(PathVeerCommand.Disable);

        _updateItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync(
                    PathVeerCommand.UpdatePrefixes);

        _prefixUpdateCheckItem.Click +=
            async (_, _) =>
                await CheckPrefixUpdatesAsync();

        _repairItem.Click +=
            async (_, _) =>
                await ExecuteCommandAsync(PathVeerCommand.Repair);

        _appUpdateCheckItem.Click +=
            async (_, _) =>
                await CheckAppUpdatesAsync();

        _configItem.Click +=
            async (_, _) => await ShowConfigurationAsync();

        _customRoutesItem.Click +=
            async (_, _) => await ShowCustomRoutesDialogAsync();

        _runtimeSnapshotItem.Click +=
            async (_, _) => await ShowRuntimeSnapshotDialogAsync();

        _executionPreviewItem.Click +=
            async (_, _) =>
                await ShowExecutionPreviewDialogAsync();

        _diagnosticsItem.Click +=
            async (_, _) => await ShowDiagnosticsDialogAsync();

        _supportBundleItem.Click +=
            async (_, _) => await ShowSupportBundleExportAsync();

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
        menu.Items.Add(_prefixUpdateCheckItem);
        menu.Items.Add(_appUpdateCheckItem);
        menu.Items.Add(_repairItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_configItem);
        menu.Items.Add(_countryItem);
        menu.Items.Add(_customRoutesItem);
        menu.Items.Add(_runtimeSnapshotItem);
        menu.Items.Add(_executionPreviewItem);
        menu.Items.Add(_diagnosticsItem);
        menu.Items.Add(_supportBundleItem);
        menu.Items.Add(_logsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "PathVeer",
            Icon = LoadApplicationIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick +=
            async (_, _) =>
                await RefreshStatusAsync(
                    showMessage: true);

        _prefixUpdateNotificationTracker =
            new PrefixUpdateNotificationTracker();
        _prefixUpdateNotificationSink =
            NullPrefixUpdateNotificationSink.Instance;

        _timer = new System.Windows.Forms.Timer
        {
            Interval = TransitionalPollIntervalMs
        };

        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();

        _ = PollAsync();

        // Begin listening for duplicate-launch "show existing" signals. The
        // primary Tray owns the session-scoped event; duplicates set it.
        _uiBridge.CreateControl();
        _showEvent = TraySingleInstance.OpenOrCreateShowEvent();
        if (_showEvent is not null)
        {
            _showWaiter = new Thread(WaitForShowSignal) { IsBackground = true };
            _showWaiter.Start();
        }

        // devsign.10 — graceful exit signal for installer quiescence. An
        // external caller (the installer) sets the session-scoped exit event to
        // ask THIS Tray to exit cleanly so shared binaries can be replaced.
        _exitEvent = TraySingleInstance.OpenOrCreateExitEvent();
        if (_exitEvent is not null)
        {
            _exitWaiter = new Thread(WaitForExitSignal) { IsBackground = true };
            _exitWaiter.Start();
        }
    }

    // Background waiter: a duplicate PathVeer.Tray launch sets the event; we
    // surface the existing Tray (balloon) on the UI thread.
    private void WaitForShowSignal()
    {
        var evt = _showEvent;
        if (evt is null) return;
        try
        {
            while (!_disposed)
            {
                if (evt.WaitOne(1000))
                {
                    ShowExisting();
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (AbandonedMutexException) { }
    }

    // Background waiter: the installer (or a controlled shutdown) sets the
    // session-scoped exit event; we run the normal ExitApplication path on the
    // UI thread so the Tray releases its single-instance mutex and resources.
    private void WaitForExitSignal()
    {
        var evt = _exitEvent;
        if (evt is null) return;
        try
        {
            while (!_disposed)
            {
                if (evt.WaitOne(1000))
                {
                    if (_uiBridge.InvokeRequired)
                    {
                        _uiBridge.Invoke((Action)ExitApplication);
                    }
                    else
                    {
                        ExitApplication();
                    }
                    return;
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (AbandonedMutexException) { }
    }

    // Surfaces the running Tray when a duplicate launch requests it.
    private void ShowExisting()
    {
        if (_uiBridge.InvokeRequired)
        {
            _uiBridge.Invoke((Action)ShowExisting);
            return;
        }

        _notifyIcon.Visible = true;
        _notifyIcon.ShowBalloonTip(
            3000, "PathVeer", "PathVeer is already running.", ToolTipIcon.Info);
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
        ServiceResponse? response =
            await RefreshStatusAsync(showMessage: false);

        if (response?.PrefixUpdateMonitor is { } monitor)
        {
            _prefixUpdateNotificationTracker.ProcessSnapshot(
                monitor,
                serviceAvailable: true,
                manualCheckSinceLastReset: false);

            if (_prefixUpdateNotificationTracker.ShouldNotify)
            {
                _prefixUpdateNotificationSink.NotifyBalloon(
                    "PathVeer",
                    "A newer Iran prefix dataset is available.");

                _prefixUpdateNotificationTracker.MarkNotified();
            }
        }
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

        _notifyIcon.Text = $"PathVeer — Service {stateText}";

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
                $"PathVeer — could not {verb} service",
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
            "the PathVeer service.\n\n" +
            "Restart the tray as administrator?",
            "PathVeer",
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
        PathVeerCommand command)
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
                    "PathVeer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else
            {
                _notifyIcon.ShowBalloonTip(
                    3000,
                    "PathVeer",
                    response.Message,
                    ToolTipIcon.Info);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "PathVeer service unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);

            await PollAsync();
        }
    }

    private async Task CheckPrefixUpdatesAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);

        ServiceResponse? response = null;

        try
        {
            response =
                await _client.SendAsync(
                    PathVeerCommand.PrefixUpdateCheckNow);

            if (!response.Success)
            {
                MessageBox.Show(
                    response.Message,
                    "PathVeer prefix update check",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else if (response.PrefixUpdateMonitor is not { }
                monitor)
            {
                MessageBox.Show(
                    "The service did not return prefix " +
                    "update monitor state.",
                    "PathVeer prefix update check",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(
                    PrefixUpdateCheckResultFormatter
                        .BuildResultText(monitor),
                    "PathVeer prefix update check",
                    MessageBoxButtons.OK,
                    PrefixUpdateCheckResultFormatter
                        .IsFailure(monitor)
                        ? MessageBoxIcon.Warning
                        : MessageBoxIcon.Information);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "PathVeer service unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);

            if (response?.PrefixUpdateMonitor is { } monitor)
            {
                _prefixUpdateNotificationTracker.ProcessSnapshot(
                    monitor,
                    serviceAvailable: true,
                    manualCheckSinceLastReset: true);
            }

            await PollAsync();
        }
    }

    private async Task CheckAppUpdatesAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);

        try
        {
            var source = TrayUpdateSourceFactory.Resolve();
            if (source is null)
            {
                MessageBox.Show(
                    "Automatic update checking is not yet configured. " +
                    "It will be enabled when PathVeer distribution is published.",
                    "PathVeer — app update check",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var verifier = BuildReleaseVerifier();
            var installed = new InstalledVersionSource(
                TrayUpdateSourceFactory.InstallManifestPath());
            var checker = new UpdateChecker(source, verifier, installed);

            var result = await checker.CheckAsync(UpdateChannel.Stable);
            MessageBox.Show(
                FormatAppUpdateResult(result),
                "PathVeer — app update check",
                MessageBoxButtons.OK,
                result.State == UpdateCheckState.UpdateAvailable
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            // Non-fatal: update-check failure must never affect routing.
            MessageBox.Show(
                "Unable to check for updates: " + exception.Message,
                "PathVeer — app update check",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static ReleaseSignatureVerifier BuildReleaseVerifier()
    {
        // Production trust root: built-in release-metadata public keys, with any
        // PATHVEER_TRUSTED_META_KEYS additions/overrides (staging, rotation).
        // Always signed-only — allowUnsigned is never enabled here, so an
        // unsigned manifest or an unknown/staging key hard-fails. With no
        // environment overrides the client still trusts the built-in production
        // key, which is the whole point of embedding it.
        return ReleaseSignatureVerifier.ForProduction();
    }

    private static string FormatAppUpdateResult(UpdateCheckResult result)
    {
        return result.State switch
        {
            UpdateCheckState.UpdateAvailable =>
                $"PathVeer {result.Manifest?.Version} is available " +
                $"(you have {result.InstalledVersion?.ToString() ?? "unknown"}).",
            UpdateCheckState.NoUpdate =>
                "PathVeer is up to date.",
            UpdateCheckState.CurrentVersionNewer =>
                "You have a newer version than the latest published release.",
            UpdateCheckState.UnsupportedInstalledVersion =>
                "Installed version could not be determined; cannot check safely.",
            UpdateCheckState.MinimumUpgradeNotMet =>
                "A direct upgrade from your version is not supported by this " +
                "release. A manual reinstall may be required.",
            UpdateCheckState.InvalidManifest =>
                "The release information was malformed.",
            UpdateCheckState.InvalidSignature =>
                "The release information signature could not be verified.",
            UpdateCheckState.IncompatibleArchitecture =>
                "The available release does not match this machine architecture.",
            UpdateCheckState.WrongProduct =>
                "The release information is not for PathVeer.",
            UpdateCheckState.WrongChannel =>
                "No release is available for your update channel.",
            UpdateCheckState.NetworkUnavailable =>
                "Could not reach the update source.",
            _ => result.Detail ?? "Update check completed."
        };
    }

    private async Task<ServiceResponse?> RefreshStatusAsync(
        bool showMessage)
    {
        if (_busy)
        {
            return null;
        }

        try
        {
            ServiceResponse response =
                await _client.SendAsync(PathVeerCommand.Status);

            if (!response.Success ||
                response.Status is null)
            {
                SetUnavailable(response.Message);
                return null;
            }

            ApplyStatus(response.Status);

            if (showMessage)
            {
                MessageBox.Show(
                    BuildStatusText(response.Status),
                    "PathVeer status",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return response;
        }
        catch
        {
            SetUnavailable("Service unavailable");
            return null;
        }
    }

    private void ApplyStatus(
        PathVeerStatus status)
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
        _prefixUpdateCheckItem.Enabled =
            PrefixUpdateMenuPolicy.IsAvailable(
                serviceRunning: true,
                _busy);
        _repairItem.Enabled =
            status.Enabled && !_busy;
        _configItem.Enabled = !_busy;
        _customRoutesItem.Enabled =
            CustomRouteMenuPolicy.IsAvailable(
                serviceRunning: true,
                _busy);
        _runtimeSnapshotItem.Enabled = !_busy;
        _executionPreviewItem.Enabled =
            ExecutionPreviewMenuPolicy.IsAvailable(
                serviceRunning: true,
                _busy);
        _diagnosticsItem.Enabled = !_busy;
        _supportBundleItem.Enabled =
            SupportBundleMenuPolicy.IsAvailable(
                serviceRunning: true,
                _busy);

        if (status.RequestedCountryCode is { } requested)
        {
            DirectCountryCode.TryParse(
                requested,
                out DirectCountryCode? requestedCountry);

            if (requestedCountry is not null)
            {
                _countryItem.Text =
                    $"Direct country: " +
                    $"{requestedCountry.DisplayName} " +
                    $"({requestedCountry.Code})";
            }
        }

        _notifyIcon.Text =
            status.Enabled
                ? "PathVeer — Enabled"
                : "PathVeer — Disabled";
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
        _prefixUpdateCheckItem.Enabled = false;
        _repairItem.Enabled = false;
        _configItem.Enabled = false;
        _customRoutesItem.Enabled = false;
        _runtimeSnapshotItem.Enabled = false;
        _executionPreviewItem.Enabled = false;
        _diagnosticsItem.Enabled = false;
        _supportBundleItem.Enabled = false;

        if (!_notifyIcon.Text.StartsWith(
                "PathVeer — Service",
                StringComparison.Ordinal))
        {
            _notifyIcon.Text =
                "PathVeer — Service unavailable";
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
        _prefixUpdateCheckItem.Enabled = false;
        _repairItem.Enabled = false;
        _configItem.Enabled = false;
        _customRoutesItem.Enabled = false;
        _runtimeSnapshotItem.Enabled = false;
        _executionPreviewItem.Enabled = false;
        _diagnosticsItem.Enabled = false;
        _supportBundleItem.Enabled = false;

        _startServiceItem.Enabled = false;
        _stopServiceItem.Enabled = false;
        _restartServiceItem.Enabled = false;
        _installServiceItem.Enabled = false;
        _uninstallServiceItem.Enabled = false;
    }

    private async Task SetCountryAsync(string code)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            ServiceResponse response =
                await _client.SendAsync(
                    PathVeerCommand.SetConfigurationDirectCountry,
                    code);

            if (!response.Success)
            {
                MessageBox.Show(
                    response.Message,
                    "PathVeer — country change failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Refresh the menu label from the returned config so the new
            // requested country is shown immediately. This does NOT enable the
            // service; if the prefix dataset could not be fetched the request
            // is still accepted (requested policy) but routing stays blocked.
            if (response.Configuration?.DirectCountryCode is { } country)
            {
                _countryItem.Text =
                    $"Direct country: {country.DisplayName} " +
                    $"({country.Code})";
            }

            if (response.PrefixRefreshed == false)
            {
                MessageBox.Show(
                    response.Message,
                    "PathVeer — country set (dataset unavailable)",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            await RefreshStatusAsync(showMessage: false);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "PathVeer service unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task ShowConfigurationAsync()
    {
        try
        {
            ServiceResponse response =
                await _client.SendAsync(
                    PathVeerCommand.GetConfiguration);

            if (!response.Success ||
                response.Configuration is null)
            {
                MessageBox.Show(
                    response.Message,
                    "PathVeer configuration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var c = response.Configuration;

            MessageBox.Show(
                $"Enabled: {c.Enabled}\n" +
                $"Direct country: {c.DirectCountryCode?.Code ?? "IR"}\n" +
                $"VPN provider: {c.VpnProvider}\n" +
                $"VPN profile: {c.VpnProfilePath}\n" +
                $"Auto repair: {c.AutoRepair}\n" +
                $"Repair interval: {c.RepairInterval}\n" +
                $"Auto update prefixes: {c.AutoUpdatePrefixes}\n" +
                $"Prefix update interval: {c.PrefixUpdateInterval}",
                "PathVeer configuration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "PathVeer service unavailable",
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
                "PathVeer custom routes",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            await PollAsync();
        }
    }

    private async Task ShowRuntimeSnapshotDialogAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            using RuntimeSnapshotDialog dialog = new(_client);
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "PathVeer runtime snapshot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            await PollAsync();
        }
    }

    private async Task ShowExecutionPreviewDialogAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            using ExecutionPreviewDialog dialog = new(_client);
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "PathVeer execution preview",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            await PollAsync();
        }
    }

    private async Task ShowDiagnosticsDialogAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            using DiagnosticReportDialog dialog = new(_client);
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "PathVeer diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            await PollAsync();
        }
    }

    private async Task ShowSupportBundleExportAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            SaveFileDialogAdapter dialog = new();

            MessageBoxStatusSink statusSink = new();

            await SupportBundleTrayFlow.ExportAsync(
                _client,
                dialog,
                statusSink,
                TimeProvider.System);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "PathVeer support bundle",
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
                "PathVeer",
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
        PathVeerStatus status)
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
                    "PathVeer.Tray.tray-icon.ico")
            ?? throw new InvalidOperationException(
                "Embedded tray icon resource is missing.");

        return new Icon(stream);
    }

    private void ExitApplication()
    {
        _disposed = true;
        _showEvent?.Set(); // wake the waiter so it observes _disposed
        _showWaiter?.Join(TimeSpan.FromSeconds(2));

        _timer.Stop();
        _timer.Dispose();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        _showEvent?.Dispose();
        _uiBridge.Dispose();

        ExitThread();
    }
}
