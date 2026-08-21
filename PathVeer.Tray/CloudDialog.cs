using System.Diagnostics;
using PathVeer.Core.Cloud;
using PathVeer.Core.Ipc;

namespace PathVeer.Tray;

/// <summary>
/// Operator UI for connecting this PathVeer installation to PathVeer Cloud.
///
/// SECURITY BOUNDARY: this dialog is input-only. It forwards the operator's
/// enrollment code + label to the Service over IPC. It NEVER receives the
/// device credential (the ServiceResponse.CloudRegistration view omits it),
/// and it clears the enrollment code from memory as soon as the connect call
/// returns. The Service — the machine authority — performs the network
/// exchange, encrypts the credential, and persists it.
/// </summary>
public sealed class CloudDialog : Form
{
    private readonly ICustomRouteCommandSender _sender;

    private readonly TextBox _statusLabel;
    private readonly TextBox _orgTextBox;
    private readonly TextBox _deviceIdTextBox;
    private readonly TextBox _heartbeatTextBox;
    private readonly TextBox _labelTextBox;

    private readonly TextBox _codeTextBox;
    private readonly Button _connectButton;
    private readonly Button _resetButton;
    private readonly Button _closeButton;
    private readonly Label _messageLabel;

    private bool _busy;

    public CloudDialog(
        ICustomRouteCommandSender? sender = null)
    {
        _sender = sender ?? new PathVeerServiceClient();

        Text = "PathVeer Cloud";
        ClientSize = new Size(460, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        Label statusCaption = Caption(12, 12, "Connection:");
        _statusLabel = Value(100, 12, "Checking…");

        Label orgCaption = Caption(12, 42, "Organization:");
        _orgTextBox = ReadOnlyValue(100, 42);

        Label deviceCaption = Caption(12, 72, "Device ID:");
        _deviceIdTextBox = ReadOnlyValue(100, 72);

        Label heartbeatCaption = Caption(12, 102, "Last heartbeat:");
        _heartbeatTextBox = ReadOnlyValue(100, 102);

        Label labelCaption = Caption(12, 132, "Device label:");
        _labelTextBox = ReadOnlyValue(100, 132);

        Label codeCaption = Caption(12, 168, "Enrollment code:");
        _codeTextBox = new TextBox
        {
            Location = new Point(100, 165),
            Size = new Size(340, 20),
            UseSystemPasswordChar = true,
            MaxLength = 256
        };

        Label deviceLabelCaption = Caption(12, 198, "Device label:");
        _labelTextBox_input = new TextBox
        {
            Location = new Point(100, 195),
            Size = new Size(340, 20),
            MaxLength = 128
        };

        _connectButton = new Button
        {
            Text = "Connect",
            Location = new Point(100, 230),
            Size = new Size(100, 28)
        };

        _resetButton = new Button
        {
            Text = "Reset / Re-enroll",
            Location = new Point(212, 230),
            Size = new Size(130, 28)
        };

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(354, 230),
            Size = new Size(86, 28)
        };

        _messageLabel = new Label
        {
            Location = new Point(12, 270),
            Size = new Size(430, 60),
            Text = ""
        };

        _connectButton.Click += async (_, _) => await ConnectAsync();
        _resetButton.Click += async (_, _) => await ResetAsync();
        _closeButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(statusCaption);
        Controls.Add(_statusLabel);
        Controls.Add(orgCaption);
        Controls.Add(_orgTextBox);
        Controls.Add(deviceCaption);
        Controls.Add(_deviceIdTextBox);
        Controls.Add(heartbeatCaption);
        Controls.Add(_heartbeatTextBox);
        Controls.Add(labelCaption);
        Controls.Add(_labelTextBox);
        Controls.Add(codeCaption);
        Controls.Add(_codeTextBox);
        Controls.Add(deviceLabelCaption);
        Controls.Add(_labelTextBox_input);
        Controls.Add(_connectButton);
        Controls.Add(_resetButton);
        Controls.Add(_closeButton);
        Controls.Add(_messageLabel);

        Shown += async (_, _) => await RefreshAsync();
    }

    private TextBox _labelTextBox_input = null!;

    private static Label Caption(int x, int y, string text) =>
        new() { Location = new Point(x, y + 3), Size = new Size(82, 16), Text = text };

    private static TextBox Value(int x, int y, string text) =>
        new() { Location = new Point(x, y), Size = new Size(340, 20), ReadOnly = true, Text = text };

    private static TextBox ReadOnlyValue(int x, int y) =>
        new() { Location = new Point(x, y), Size = new Size(340, 20), ReadOnly = true };

    private async Task RefreshAsync()
    {
        SetBusy(true);
        _messageLabel.Text = "";

        try
        {
            ServiceResponse response =
                await _sender.SendAsync(PathVeerCommand.CloudStatus);

            CloudRegistrationView? view = response.CloudRegistration;
            if (view is null)
            {
                _statusLabel.Text = "Not connected";
                return;
            }

            _statusLabel.Text = view.State switch
            {
                CloudConnectionState.Connected => "Connected",
                CloudConnectionState.Revoked => "Revoked — re-enrollment required",
                CloudConnectionState.Degraded => "Degraded (cloud unreachable)",
                CloudConnectionState.EnrollmentFailed => "Enrollment failed",
                _ => "Not connected"
            };
            _orgTextBox.Text = view.OrganizationId ?? "—";
            _deviceIdTextBox.Text = view.DeviceId ?? "—";
            _heartbeatTextBox.Text = view.LastHeartbeatUtc
                ?.ToLocalTime().ToString("g") ?? "—";
            _labelTextBox.Text = view.DeviceLabel ?? "—";

            bool enrolled = view.State is CloudConnectionState.Connected
                or CloudConnectionState.Degraded
                or CloudConnectionState.Revoked;
            _codeTextBox.Enabled = !enrolled;
            _labelTextBox_input.Enabled = !enrolled;
            _connectButton.Enabled = !enrolled;
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "Unavailable";
            _messageLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ConnectAsync()
    {
        // Take the code into a local, then clear the text box immediately so
        // it does not linger in the control's text buffer.
        string code = _codeTextBox.Text;
        _codeTextBox.Text = "";

        if (string.IsNullOrWhiteSpace(code))
        {
            _messageLabel.Text = "Enter the enrollment code from PathVeer Cloud.";
            return;
        }

        string label = _labelTextBox_input.Text?.Trim() ?? "";
        // Do not send the Windows hostname; the label is operator-entered only.
        if (string.Equals(
                label,
                Environment.MachineName,
                StringComparison.OrdinalIgnoreCase))
        {
            label = "";
        }

        SetBusy(true);
        _messageLabel.Text = "Connecting…";

        try
        {
            ServiceResponse response =
                await _sender.SendAsync(
                    PathVeerCommand.CloudEnroll,
                    value: code,
                    description: label);

            if (!response.Success)
            {
                _messageLabel.Text = response.Message;
                return;
            }

            _messageLabel.Text = "Connected to PathVeer Cloud.";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _messageLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ResetAsync()
    {
        DialogResult confirm = MessageBox.Show(
            this,
            "Clear the local PathVeer Cloud registration? The device must be " +
            "re-enrolled with a new code to reconnect. This does not contact " +
            "Cloud.",
            "PathVeer Cloud",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        _messageLabel.Text = "Resetting…";

        try
        {
            ServiceResponse response =
                await _sender.SendAsync(
                    PathVeerCommand.CloudReset,
                    force: true);

            if (!response.Success)
            {
                _messageLabel.Text = response.Message;
                return;
            }

            _messageLabel.Text = "Local registration cleared.";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _messageLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _connectButton.Enabled = !busy && _codeTextBox.Enabled;
        _resetButton.Enabled = !busy;
        _codeTextBox.Enabled = !busy;
        _labelTextBox_input.Enabled = !busy;
    }
}
