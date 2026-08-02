using IranDirect.Core.Diagnostics;
using IranDirect.Core.Ipc;

namespace IranDirect.Tray;

public sealed class DiagnosticReportDialog : Form
{
    private readonly ICustomRouteCommandSender _sender;
    private readonly Action<string>? _showError;

    private readonly TableLayoutPanel _panel;
    private readonly Button _refreshButton;
    private readonly Button _copyButton;
    private readonly Button _closeButton;
    private readonly Label _statusLabel;
    private bool _busy;
    private DiagnosticReport? _currentReport;

    public DiagnosticReportDialog(
        ICustomRouteCommandSender? sender = null,
        Action<string>? showError = null)
    {
        _sender = sender ?? new IranDirectServiceClient();
        _showError = showError;

        Text = "Run Diagnostics...";
        ClientSize = new Size(520, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _panel = new TableLayoutPanel
        {
            Location = new Point(12, 12),
            Size = new Size(496, 510),
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
            Location = new Point(12, 532),
            Size = new Size(90, 28)
        };

        _copyButton = new Button
        {
            Text = "Copy",
            Location = new Point(108, 532),
            Size = new Size(90, 28)
        };

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(204, 532),
            Size = new Size(90, 28)
        };

        _statusLabel = new Label
        {
            Location = new Point(12, 568),
            Size = new Size(496, 20),
            Text = "Loading..."
        };

        _refreshButton.Click +=
            async (_, _) => await RefreshAsync();

        _copyButton.Click += (_, _) => CopyToClipboard();

        _closeButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(_panel);
        Controls.Add(_refreshButton);
        Controls.Add(_copyButton);
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
                    IranDirectCommand.Diagnostics);

            if (!response.Success ||
                response.Report is null)
            {
                ShowError(response.Message);
                _statusLabel.Text =
                    "Failed to load diagnostics.";
                return;
            }

            Bind(response.Report);
            _statusLabel.Text =
                $"Captured: " +
                $"{response.Report.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _statusLabel.Text =
                "Failed to load diagnostics.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopyToClipboard()
    {
        if (_currentReport is null)
        {
            return;
        }

        string text =
            DiagnosticReportDialogModel.GetCopyText(
                _currentReport);

        Clipboard.SetText(text);
    }

    private void Bind(DiagnosticReport report)
    {
        _currentReport = report;

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        _panel.SuspendLayout();
        _panel.Controls.Clear();
        _panel.RowCount = 0;

        AddOverallStatusSection(display.OverallStatus);
        AddSummarySection(display.Summary);

        foreach (DiagnosticCategorySection section in
                 display.Categories)
        {
            AddCategorySection(section);
        }

        _panel.ResumeLayout(true);
    }

    private void AddOverallStatusSection(
        DiagnosticOverallStatus status)
    {
        Label header = new()
        {
            Text = "Overall Status",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 2)
        };

        int headerRow = _panel.RowCount;
        _panel.RowCount++;
        _panel.Controls.Add(header, 0, headerRow);
        _panel.SetColumnSpan(header, 2);

        Label statusLabel = new()
        {
            Text = status.Text,
            Font = new Font(
                Font,
                status.IsHealthy
                    ? FontStyle.Bold
                    : FontStyle.Regular),
            ForeColor = status.IsHealthy
                ? Color.DarkGreen
                : status.Text == "Failed"
                    ? Color.DarkRed
                    : Color.DarkOrange,
            AutoSize = true,
            Margin = new Padding(24, 0, 8, 0)
        };

        int statusRow = _panel.RowCount;
        _panel.RowCount++;
        _panel.Controls.Add(statusLabel, 0, statusRow);
        _panel.SetColumnSpan(statusLabel, 2);
    }

    private void AddSummarySection(
        IReadOnlyList<DiagnosticSummaryRow> rows)
    {
        Label header = new()
        {
            Text = "Summary",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 2)
        };

        int headerRow = _panel.RowCount;
        _panel.RowCount++;
        _panel.Controls.Add(header, 0, headerRow);
        _panel.SetColumnSpan(header, 2);

        foreach (DiagnosticSummaryRow row in rows)
        {
            Label label = new()
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
            _panel.Controls.Add(label, 0, rowIndex);
            _panel.Controls.Add(value, 1, rowIndex);
        }
    }

    private void AddCategorySection(
        DiagnosticCategorySection section)
    {
        Label header = new()
        {
            Text = section.CategoryName,
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 2)
        };

        int headerRow = _panel.RowCount;
        _panel.RowCount++;
        _panel.Controls.Add(header, 0, headerRow);
        _panel.SetColumnSpan(header, 2);

        foreach (DiagnosticCheckRow check in section.Checks)
        {
            Label icon = new()
            {
                Text = $"[{check.StatusIcon}]",
                AutoSize = true,
                ForeColor = check.StatusIcon == "X"
                    ? Color.DarkRed
                    : check.StatusIcon == "!"
                        ? Color.DarkOrange
                        : Color.DarkGreen,
                Margin = new Padding(24, 0, 4, 0)
            };

            Label title = new()
            {
                Text = check.Title,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0)
            };

            int titleRow = _panel.RowCount;
            _panel.RowCount++;

            Panel titlePanel = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            titlePanel.Controls.Add(icon);
            title.Left = icon.Right + 2;
            titlePanel.Controls.Add(title);
            titlePanel.Size = new Size(
                title.Right + 4, title.Height);

            _panel.Controls.Add(titlePanel, 0, titleRow);
            _panel.SetColumnSpan(titlePanel, 2);

            Label message = new()
            {
                Text = check.Message,
                AutoSize = true,
                Margin = new Padding(48, 0, 8, 0),
                MaximumSize = new Size(440, 0)
            };

            int messageRow = _panel.RowCount;
            _panel.RowCount++;
            _panel.Controls.Add(message, 0, messageRow);
            _panel.SetColumnSpan(message, 2);

            if (check.SuggestedAction is not null)
            {
                Label suggestion = new()
                {
                    Text = $"-> {check.SuggestedAction}",
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Margin = new Padding(48, 0, 8, 0),
                    MaximumSize = new Size(440, 0)
                };

                int suggestionRow = _panel.RowCount;
                _panel.RowCount++;
                _panel.Controls.Add(
                    suggestion, 0, suggestionRow);
                _panel.SetColumnSpan(suggestion, 2);
            }
        }
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
            "Run Diagnostics",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _refreshButton.Enabled = !busy;
        _copyButton.Enabled = !busy;
    }
}
