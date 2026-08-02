using IranDirect.Core.Ipc;
using IranDirect.Core.Observability;

namespace IranDirect.Tray;

public sealed class RuntimeSnapshotDialog : Form
{
    private readonly ICustomRouteCommandSender _sender;
    private readonly Action<string>? _showError;

    private readonly TableLayoutPanel _panel;
    private readonly Button _refreshButton;
    private readonly Button _closeButton;
    private readonly Label _statusLabel;
    private bool _busy;

    public RuntimeSnapshotDialog(
        ICustomRouteCommandSender? sender = null,
        Action<string>? showError = null)
    {
        _sender = sender ?? new IranDirectServiceClient();
        _showError = showError;

        Text = "Runtime Snapshot";
        ClientSize = new Size(470, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _panel = new TableLayoutPanel
        {
            Location = new Point(12, 12),
            Size = new Size(446, 470),
            ColumnCount = 2,
            AutoScroll = true,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };

        _panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 180));
        _panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        _refreshButton = new Button
        {
            Text = "Refresh",
            Location = new Point(12, 492),
            Size = new Size(90, 28)
        };

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(108, 492),
            Size = new Size(90, 28)
        };

        _statusLabel = new Label
        {
            Location = new Point(12, 528),
            Size = new Size(446, 20),
            Text = "Loading..."
        };

        _refreshButton.Click +=
            async (_, _) => await RefreshAsync();

        _closeButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(_panel);
        Controls.Add(_refreshButton);
        Controls.Add(_closeButton);
        Controls.Add(_statusLabel);

        Shown += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        SetBusy(true);
        _statusLabel.Text = "Loading...";

        try
        {
            ServiceResponse response =
                await _sender.SendAsync(
                    IranDirectCommand.RuntimeSnapshot);

            if (!response.Success || response.Snapshot is null)
            {
                ShowError(response.Message);
                _statusLabel.Text = "Failed to load snapshot.";
                return;
            }

            Bind(response.Snapshot);
            _statusLabel.Text =
                $"Captured: " +
                $"{response.Snapshot.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _statusLabel.Text = "Failed to load snapshot.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Bind(RuntimeSnapshot snapshot)
    {
        _panel.SuspendLayout();
        _panel.Controls.Clear();
        _panel.RowCount = 0;

        foreach (SnapshotSection section in
                 RuntimeSnapshotDialogModel.MapSections(snapshot))
        {
            Label header = new()
            {
                Text = section.Title,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 2)
            };

            int headerRow = _panel.RowCount;
            _panel.RowCount++;
            _panel.Controls.Add(header, 0, headerRow);
            _panel.SetColumnSpan(header, 2);

            foreach (SnapshotSectionRow row in section.Rows)
            {
                Label name = new()
                {
                    Text = row.Label,
                    AutoSize = true,
                    Margin = new Padding(24, 0, 8, 0)
                };

                Label value = new()
                {
                    Text = row.Value,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 0)
                };

                int rowIndex = _panel.RowCount;
                _panel.RowCount++;
                _panel.Controls.Add(name, 0, rowIndex);
                _panel.Controls.Add(value, 1, rowIndex);
            }
        }

        _panel.ResumeLayout(true);
    }

    private void ShowError(string message)
    {
        if (_showError is not null)
        {
            _showError(message);
            return;
        }

        MessageBox.Show(
            this,
            message,
            "Runtime Snapshot",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _refreshButton.Enabled = !busy;
    }
}
