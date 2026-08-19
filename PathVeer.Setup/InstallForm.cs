using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using PathVeer.Core.Installer;

namespace PathVeer.Setup;

/// <summary>
/// Phase 37.2 consumer installer window (WinForms).
///
/// Design rules (per the phase brief):
///   * The UI owns NO install/migration/state logic. It collects intent,
///     calls InstallController (which delegates to the embedded
///     Install-PathVeer.ps1 contract), and renders structured progress.
///   * Tray startup and Service authority are kept distinct in wording.
///   * Unattended mode (/quiet) performs the same operation with no window.
/// </summary>
public sealed class InstallForm : Form
{
    private readonly InstallController _controller;
    private readonly string _targetVersion;
    private InstallScenario _scenario;
    private InstallState _state = new();

    private TableLayoutPanel _root = null!;
    private Label _titleLabel = null!;
    private Label _subtitleLabel = null!;
    private Panel _optionsPanel = null!;
    private CheckBox _trayStartupCheck = null!;
    private CheckBox _launchAfterCheck = null!;
    private CheckBox _purgeCheck = null!;
    private Label _optionsHint = null!;
    private ProgressBar _progress = null!;
    private Label _stageLabel = null!;
    private TextBox _logBox = null!;
    private FlowLayoutPanel _buttons = null!;
    private Button _primaryButton = null!;
    private Button _secondaryButton = null!;
    private Button _cancelButton = null!;
    private Label _resultLabel = null!;

    private readonly StringBuilder _log = new();

    /// <summary>
    /// Explicit, authoritative result contract between the interactive form and
    /// <c>Program.Main</c>. Defaults to <see cref="SetupExitCodes.UserCancelled"/>
    /// (closing before the operation runs == user cancel) so a window that is
    /// dismissed without an operation result is NEVER silently reported as
    /// <see cref="SetupExitCodes.Success"/> (NEW ISSUE #2). Success/failure set it
    /// explicitly; closing the result window never erases it.
    /// </summary>
    public int ResultExitCode { get; private set; } = SetupExitCodes.UserCancelled;

#if DEBUG
    /// <summary>
    /// Test seam: applies the SAME result contract as <see cref="OnCompleted"/>
    /// but without the WinForms <c>Invoke</c> hop, so unit tests can drive the
    /// latch without a live message loop. Production code uses
    /// <see cref="OnCompleted"/> (which calls this path on the UI thread).
    /// </summary>
    public void SimulateCompleted(ResultRecord result)
    {
        if (result.Success)
        {
            TerminalShowSuccess(result);
        }
        else
        {
            TerminalShowFailure(SetupExitCodes.MapResultCategory(result.Category));
        }
    }

    // --- DEBUG test seams: expose the SAME fail-closed logic the RunOperation
    // worker uses, so unit tests can drive the post-return terminal rule without
    // spawning a real PowerShell operation. ---
    public bool DebugTerminalSignaled() => _terminalTransitioned.IsSet;

    public void DebugFailClosedContractViolation()
    {
        if (this.IsDisposed) return;
        TerminalShowFailure(SetupExitCodes.ContractViolation);
    }
#endif

    public InstallForm(InstallController controller, string targetVersion)
    {
        _controller = controller;
        _targetVersion = targetVersion;
        InitializeComponent();
        ApplyHighDpi();
        LoadProductIcon();
        _controller.Progress += OnProgress;
        _controller.LogLine += OnLog;
        _controller.Completed += OnCompleted;
        DetectAndRender();
    }

    // Branded title-bar / taskbar icon, loaded from the embedded product ico
    // (the canonical PathVeer icon, shared with the Tray). Falls back silently
    // to the system default if the resource is missing.
    private void LoadProductIcon()
    {
        try
        {
            var asm = typeof(InstallForm).Assembly;
            using var stream = asm.GetManifestResourceStream(
                "PathVeer.Setup.Resources.tray-icon.ico");
            if (stream is not null)
            {
                using var icon = new Icon(stream);
                this.Icon = (Icon)icon.Clone();
            }
        }
        catch
        {
            // Non-fatal: the form works without a custom icon.
        }
    }

    private void ApplyHighDpi()
    {
        // WinForms per-monitor DPI awareness is set via the app manifest;
        // this ensures the form scales its controls on 125%-200% displays.
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.AutoScaleDimensions = new SizeF(96F, 96F);
    }

    private void InitializeComponent()
    {
        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(16),
        };
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));   // title
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // subtitle
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));   // options
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // progress
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // log
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // result
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // buttons

        _titleLabel = new Label
        {
            Text = "PathVeer Setup",
            Font = new Font(this.Font, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _subtitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _optionsPanel = new Panel { Dock = DockStyle.Fill };
        _trayStartupCheck = new CheckBox
        {
            Text = "Start PathVeer Tray when I sign in",
            Checked = true,
            Left = 0, Top = 4, Width = 320, AutoSize = true,
        };
        _launchAfterCheck = new CheckBox
        {
            Text = "Launch PathVeer Tray after setup",
            Checked = true,
            Left = 0, Top = 30, Width = 320, AutoSize = true,
        };
        _purgeCheck = new CheckBox
        {
            Text = "Also delete saved PathVeer settings and state",
            Checked = false,
            Left = 0, Top = 56, Width = 360, AutoSize = true,
        };
        _optionsHint = new Label
        {
            Left = 0, Top = 78, Width = 360, AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };
        _optionsPanel.Controls.AddRange(new Control[]
            { _trayStartupCheck, _launchAfterCheck, _purgeCheck, _optionsHint });

        _progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
        _stageLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        var progressPanel = new Panel { Dock = DockStyle.Fill };
        _stageLabel.Height = 18;
        _stageLabel.Dock = DockStyle.Top;
        _progress.Dock = DockStyle.Fill;
        progressPanel.Controls.Add(_progress);
        progressPanel.Controls.Add(_stageLabel);

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
        };

        _resultLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        _buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0),
        };
        _primaryButton = new Button { Text = "Install", Width = 110, Height = 32 };
        _secondaryButton = new Button { Text = "Cancel", Width = 110, Height = 32 };
        _cancelButton = new Button { Text = "Finish", Width = 110, Height = 32, Visible = false };
        _buttons.Controls.AddRange(new Control[] { _primaryButton, _secondaryButton });

        _root.Controls.Add(_titleLabel, 0, 0);
        _root.Controls.Add(_subtitleLabel, 0, 1);
        _root.Controls.Add(_optionsPanel, 0, 2);
        _root.Controls.Add(progressPanel, 0, 3);
        _root.Controls.Add(_logBox, 0, 4);
        _root.Controls.Add(_resultLabel, 0, 5);
        _root.Controls.Add(_buttons, 0, 6);

        this.Controls.Add(_root);
        this.Text = "PathVeer Setup";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MinimumSize = new Size(520, 460);
        this.ClientSize = new Size(560, 480);

        _primaryButton.Click += OnPrimary;
        _secondaryButton.Click += OnSecondary;
        _cancelButton.Click += (_, _) => Close();
    }

    private void DetectAndRender()
    {
        _state = _controller.DetectState();
        _scenario = InstallStateClassifier.Classify(_state, _targetVersion);
        RenderForScenario();
    }

    private void RenderForScenario()
    {
        _progress.Visible = false;
        _stageLabel.Visible = false;
        _resultLabel.Text = "";
        _optionsPanel.Visible = true;
        _trayStartupCheck.Visible = true;
        _launchAfterCheck.Visible = true;
        _purgeCheck.Visible = false;
        _purgeCheck.Checked = false;
        _secondaryButton.Visible = true;
        _cancelButton.Visible = false;
        _buttons.Controls.Clear();
        _buttons.Controls.AddRange(new Control[] { _primaryButton, _secondaryButton });

        switch (_scenario)
        {
            case InstallScenario.NotInstalled:
                _subtitleLabel.Text = $"Install PathVeer {_targetVersion}.";
                _primaryButton.Text = "Install";
                _optionsHint.Text = "PathVeer installs a background Service (routing authority) and an optional Tray.";
                break;

            case InstallScenario.Upgrade:
                _subtitleLabel.Text =
                    $"PathVeer {_state.InstalledVersion} is installed. Setup will upgrade it to {_targetVersion}.";
                _primaryButton.Text = "Upgrade";
                _optionsHint.Text = "Your settings and routing state will be kept.";
                break;

            case InstallScenario.SameVersion:
                _subtitleLabel.Text = $"PathVeer {_targetVersion} is already installed.";
                _primaryButton.Text = "Repair";
                _secondaryButton.Text = "Uninstall";
                _optionsHint.Text = "Repair reinstalls the current version and preserves your state.";
                break;

            case InstallScenario.Downgrade:
                _subtitleLabel.Text =
                    $"A newer version of PathVeer ({_state.InstalledVersion}) is already installed.";
                _primaryButton.Text = "Close";
                _optionsHint.Text = SetupExitCodes.Describe(SetupExitCodes.DowngradeBlocked);
                LockOptionsForBlocked();
                break;

            case InstallScenario.SupportedLegacyMigration:
                _subtitleLabel.Text =
                    "A supported IranDirect installation was detected. PathVeer will migrate it " +
                    "while preserving your existing routing configuration and saved state.";
                _primaryButton.Text = "Upgrade";
                _optionsHint.Text = "Your settings and routing state will be kept.";
                break;

            case InstallScenario.ConflictingAuthority:
                _subtitleLabel.Text =
                    "Both IranDirect and PathVeer services are present. This must be resolved before installing.";
                _primaryButton.Text = "Close";
                _optionsHint.Text = "Run the IranDirect-aware migration or contact support before continuing.";
                LockOptionsForBlocked();
                break;

            case InstallScenario.PartialOrBroken:
                _subtitleLabel.Text =
                    "A partial PathVeer installation was detected. Setup can repair it.";
                _primaryButton.Text = "Repair";
                _secondaryButton.Text = "Uninstall";
                _optionsHint.Text = "Repair will reinstall the current version and preserve your state.";
                break;
        }
    }

    private void LockOptionsForBlocked()
    {
        _optionsPanel.Enabled = false;
        _primaryButton.Enabled = false;
    }

    private void OnPrimary(object? sender, EventArgs e)
    {
        switch (_scenario)
        {
            case InstallScenario.SameVersion:
                RunRepair();
                break;
            case InstallScenario.PartialOrBroken:
                RunRepair();
                break;
            case InstallScenario.Downgrade:
            case InstallScenario.ConflictingAuthority:
                Close();
                break;
            default:
                RunInstall();
                break;
        }
    }

    private void OnSecondary(object? sender, EventArgs e)
    {
        if (_scenario == InstallScenario.SameVersion || _scenario == InstallScenario.PartialOrBroken)
        {
            // Uninstall flow.
            _purgeCheck.Visible = true;
            _optionsHint.Text = "Your settings and routing state will be kept in case you reinstall later. " +
                               "Select the checkbox only to delete them.";
            _primaryButton.Text = "Uninstall";
            _secondaryButton.Text = "Cancel";
            _secondaryButton.Click -= OnSecondary;
            _secondaryButton.Click += (_, _) => RenderForScenario();
            _primaryButton.Click -= OnPrimary;
            _primaryButton.Click += (_, _) => RunUninstall();
        }
        else
        {
            Close();
        }
    }

    private void BeginMutationUI(string stageIntro)
    {
        _optionsPanel.Enabled = false;
        _primaryButton.Enabled = false;
        _secondaryButton.Enabled = false;
        _progress.Visible = true;
        _stageLabel.Visible = true;
        _stageLabel.Text = stageIntro;
        _resultLabel.Text = "";
        _log.Clear();
        _logBox.Clear();
    }

    private void RunInstall()
    {
        BeginMutationUI("Installing PathVeer...");
        var options = new InstallOptions
        {
            InstallTray = _trayStartupCheck.Checked,
            LaunchTrayAfterInstall = _launchAfterCheck.Checked,
            RegisterShell = true,
        };
        RunOperation(() => _controller.RunInstall(options));
    }

    private void RunRepair()
    {
        BeginMutationUI("Repairing PathVeer...");
        var options = new InstallOptions
        {
            InstallTray = _trayStartupCheck.Checked,
            LaunchTrayAfterInstall = _launchAfterCheck.Checked,
            RegisterShell = true,
        };
        RunOperation(() => _controller.RunInstall(options));
    }

    private void RunUninstall()
    {
        BeginMutationUI("Removing PathVeer...");
        var options = new UninstallOptions { PurgeState = _purgeCheck.Checked };
        RunOperation(() => _controller.RunUninstall(options, registerShell: true));
    }

    private void RunOperation(Func<int> operation)
    {
        int exitCode = SetupExitCodes.GenericFailure;
        var worker = new Thread(() =>
        {
            try
            {
                exitCode = operation();
            }
            catch (Exception ex)
            {
                SafeLog("ERROR: " + ex.Message);
                exitCode = SetupExitCodes.GenericFailure;
            }

            // Fail-closed completion rule:
            // The operation thread has now RETURNED. A well-formed run raises the
            // controller's Completed event (OnCompleted) which performs the single
            // terminal UI transition. If that transition never arrived (dropped
            // invoke, disposed form, lost subscriber, or a backend that returns
            // without writing a result record), the UI must NOT remain stuck on
            // the "Working" marquee forever. Wait briefly for the real terminal
            // transition; if it has not happened, surface an explicit non-zero
            // ContractViolation so the user gets a result and the process exits
            // non-zero instead of hanging.
            bool terminal = _terminalTransitioned.Wait(TimeSpan.FromSeconds(2));
            if (!terminal)
            {
                this.Invoke(() =>
                {
                    if (this.IsDisposed) return;
                    TerminalShowFailure(SetupExitCodes.ContractViolation);
                });
            }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    // Guards the single terminal UI transition (success OR failure). Set inside
    // the UI-thread transition so the fail-closed worker check and the real
    // Completed callback can never both run, and re-entrancy cannot produce a
    // second transition.
    private readonly ManualResetEventSlim _terminalTransitioned = new(false);
    private bool _completedFired;

    private void OnCompleted(ResultRecord result)
    {
        if (this.IsDisposed) return;
        this.Invoke(() =>
        {
            if (this.IsDisposed) return;
            if (result.Success)
            {
                ResultExitCode = SetupExitCodes.Success;
                TerminalShowSuccess(result);
            }
            else
            {
                ResultExitCode = SetupExitCodes.MapResultCategory(result.Category);
                TerminalShowFailure(ResultExitCode);
            }
        });
    }

    // Idempotent terminal transition. Exactly one of these runs per operation.
    private void TerminalShowSuccess(ResultRecord result)
    {
        if (_terminalTransitioned.IsSet) return;
        _terminalTransitioned.Set();
        _completedFired = true;
        ResultExitCode = SetupExitCodes.Success;
        ShowSuccess(result);
    }

    private void TerminalShowFailure(int exitCode)
    {
        if (_terminalTransitioned.IsSet) return;
        _terminalTransitioned.Set();
        _completedFired = true;
        ShowFailure(exitCode);
    }

    private void OnProgress(ProgressRecord rec)
    {
        if (this.IsDisposed) return;
        this.Invoke(() =>
        {
            if (_stageLabel.IsDisposed) return;
            _stageLabel.Text = HumanizeStage(rec.Stage);
        });
    }

    private void OnLog(string line)
    {
        if (this.IsDisposed) return;
        SafeLog(line);
    }

    private void SafeLog(string line)
    {
        this.Invoke(() =>
        {
            if (_logBox.IsDisposed) return;
            _log.AppendLine(line);
            _logBox.Text = _log.ToString();
            _logBox.SelectionStart = _logBox.Text.Length;
            _logBox.ScrollToCaret();
        });
    }

    private void ShowSuccess(ResultRecord result)
    {
        _progress.Visible = false;
        _stageLabel.Visible = false;
        _optionsPanel.Enabled = true;
        _primaryButton.Visible = false;
        _secondaryButton.Visible = false;
        _cancelButton.Visible = true;
        _buttons.Controls.Clear();
        _buttons.Controls.Add(_cancelButton);

        string verdict = $"PathVeer {_targetVersion} was installed successfully.";
        if (_scenario == InstallScenario.SupportedLegacyMigration)
        {
            verdict = "IranDirect was upgraded to PathVeer. Your configuration and state were preserved.";
        }
        else if (_scenario == InstallScenario.SameVersion)
        {
            verdict = $"PathVeer {_targetVersion} was repaired successfully.";
        }
        else if (_scenario == InstallScenario.Upgrade)
        {
            verdict = $"PathVeer was upgraded to {_targetVersion} successfully.";
        }

        _resultLabel.ForeColor = Color.Green;
        _resultLabel.Text = verdict +
            " PathVeer is installed and running. You can use the PathVeer Tray to " +
            "control routing settings. Closing the Tray does not stop the PathVeer Service.";

        if (_launchAfterCheck.Checked && (_scenario is InstallScenario.NotInstalled or
            InstallScenario.Upgrade or InstallScenario.SameVersion or
            InstallScenario.PartialOrBroken or InstallScenario.SupportedLegacyMigration))
        {
            LaunchTray();
        }
    }

    private void ShowFailure(int exitCode)
    {
        // Latch the failure code into the contract so Program.Main returns it
        // regardless of which path produced the failure (Completed event or the
        // worker fallback). Closing the result window must NOT erase it.
        ResultExitCode = exitCode;

        _progress.Visible = false;
        _stageLabel.Visible = false;
        _optionsPanel.Enabled = true;
        _primaryButton.Visible = false;
        _secondaryButton.Visible = false;
        _cancelButton.Visible = true;
        _buttons.Controls.Clear();
        _buttons.Controls.Add(_cancelButton);

        _resultLabel.ForeColor = Color.FromArgb(192, 0, 0);
        _resultLabel.Text = SetupExitCodes.Describe(exitCode) +
            " Log: " + LogPathHint();
    }

    private static string LogPathHint() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PathVeer", "Logs", "Setup");

    // The Tray is a per-user UI/controller and MUST NOT run elevated. The
    // installer runs elevated (self-elevation gate), so launching the Tray
    // directly from here would inherit the elevated token. Instead, this writes
    // a sentinel the NON-elevated parent process consumes after the elevated
    // child returns (see Program.Main / RelaunchElevated), guaranteeing the Tray
    // is spawned by the ordinary user token. This is verifiable by construction:
    // the parent is the un-elevated Explorer-launched Setup.
    private void LaunchTray()
    {
        try
        {
            WriteLaunchTraySentinel();
        }
        catch
        {
            // Non-fatal: the user can launch the Tray from the Start Menu.
        }
    }

    public static void WriteLaunchTraySentinel()
    {
        try
        {
            string sentinel = Path.Combine(
                Path.GetTempPath(), "PathVeer.Setup.LaunchTray.sentinel");
            File.WriteAllText(sentinel, "1");
        }
        catch
        {
            // Best-effort; Start Menu remains a fallback.
        }
    }

    // Consumed by the non-elevated parent: if the sentinel exists, launch the
    // Tray as the ordinary (non-elevated) user and remove the sentinel. Called
    // from Program after the elevated child exits, so the Tray never inherits
    // installer elevation.
    public static void ConsumeLaunchTraySentinel()
    {
        string sentinel = Path.Combine(
            Path.GetTempPath(), "PathVeer.Setup.LaunchTray.sentinel");
        if (!File.Exists(sentinel)) return;

        try { File.Delete(sentinel); } catch { /* ignore */ }

        string trayExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PathVeer", "Tray", "PathVeer.Tray.exe");
        if (File.Exists(trayExe))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = trayExe,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Non-fatal: the user can launch the Tray from the Start Menu.
            }
        }
    }

    private static string HumanizeStage(string stage) => stage switch
    {
        ProgressStages.VerifyingPackage => "Verifying package...",
        ProgressStages.StoppingLegacy => "Stopping previous installation...",
        ProgressStages.StoppingService => "Stopping PathVeer...",
        ProgressStages.Installing => "Installing PathVeer...",
        ProgressStages.StartingService => "Starting PathVeer Service...",
        ProgressStages.CreatingShortcuts => "Creating shortcuts...",
        ProgressStages.RemovingShortcuts => "Removing shortcuts...",
        ProgressStages.ReleasingRoutes => "Releasing managed routes...",
        ProgressStages.Finished => "Finishing...",
        _ => "Working...",
    };
}
