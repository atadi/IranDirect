using PathVeer.Core.Ipc;
using PathVeer.Core.Planning;

namespace PathVeer.Tray;

public sealed class ExecutionPreviewDialog : Form
{
    private readonly ICustomRouteCommandSender _sender;
    private readonly Action<string>? _showError;

    private readonly TableLayoutPanel _panel;
    private readonly Button _refreshButton;
    private readonly Button _copyButton;
    private readonly Button _closeButton;
    private readonly Label _statusLabel;
    private bool _busy;
    private ExecutionPreview? _currentPreview;

    public ExecutionPreviewDialog(
        ICustomRouteCommandSender? sender = null,
        Action<string>? showError = null)
    {
        _sender = sender ?? new PathVeerServiceClient();
        _showError = showError;

        Text = "Preview Next Repair...";
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
                    PathVeerCommand.ExecutionPreview);

            if (!response.Success ||
                response.Preview is null)
            {
                ShowError(response.Message);
                _statusLabel.Text =
                    "Failed to load execution preview.";
                return;
            }

            Bind(response.Preview);
            _statusLabel.Text =
                response.Message;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _statusLabel.Text =
                "Failed to load execution preview.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopyToClipboard()
    {
        if (_currentPreview is null)
        {
            return;
        }

        string text =
            ExecutionPreviewDialogModel.GetCopyText(
                _currentPreview);

        Clipboard.SetText(text);
    }

    private void Bind(ExecutionPreview preview)
    {
        _currentPreview = preview;

        ExecutionPreviewDisplay display =
            ExecutionPreviewDialogModel.Map(preview);

        _panel.SuspendLayout();
        _panel.Controls.Clear();
        _panel.RowCount = 0;

        AddSummarySection(display.Summary);

        if (!display.HasChanges)
        {
            AddNoChangesSection();
        }
        else
        {
            AddStepsHeader();

            foreach (ExecutionPreviewStepDetail step in
                     display.Steps)
            {
                AddStepRow(step);
            }
        }

        _panel.ResumeLayout(true);
    }

    private void AddSummarySection(
        IReadOnlyList<ExecutionPreviewSummaryRow> rows)
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

        foreach (ExecutionPreviewSummaryRow row in rows)
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

    private void AddNoChangesSection()
    {
        Label noChanges = new()
        {
            Text = "No changes would be applied.",
            Font = new Font(Font, FontStyle.Italic),
            AutoSize = true,
            Margin = new Padding(24, 12, 0, 0)
        };

        int row = _panel.RowCount;
        _panel.RowCount++;
        _panel.Controls.Add(noChanges, 0, row);
        _panel.SetColumnSpan(noChanges, 2);
    }

    private void AddStepsHeader()
    {
        Label header = new()
        {
            Text = "Planned Steps",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 2)
        };

        int headerRow = _panel.RowCount;
        _panel.RowCount++;
        _panel.Controls.Add(header, 0, headerRow);
        _panel.SetColumnSpan(header, 2);
    }

    private void AddStepRow(ExecutionPreviewStepDetail step)
    {
        string opIcon = step.Row.Operation switch
        {
            "Create" => "+",
            "Delete" => "-",
            "Verify" => "=",
            _ => "?"
        };

        Color opColor = step.Row.Operation switch
        {
            "Create" => Color.DarkGreen,
            "Delete" => Color.DarkRed,
            "Verify" => Color.Gray,
            _ => Color.Black
        };

        Label opLabel = new()
        {
            Text = $"[{opIcon}]",
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = opColor,
            AutoSize = true,
            Margin = new Padding(24, 0, 4, 0)
        };

        Label titleLabel = new()
        {
            Text =
                $"{step.Row.Category}: {step.Row.Target}",
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
        titlePanel.Controls.Add(opLabel);
        titleLabel.Left = opLabel.Right + 2;
        titlePanel.Controls.Add(titleLabel);
        titlePanel.Size = new Size(
            titleLabel.Right + 4, titleLabel.Height);

        _panel.Controls.Add(titlePanel, 0, titleRow);
        _panel.SetColumnSpan(titlePanel, 2);

        Label reasonLabel = new()
        {
            Text = step.Row.Reason,
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(48, 0, 8, 0),
            MaximumSize = new Size(440, 0)
        };

        int reasonRow = _panel.RowCount;
        _panel.RowCount++;
        _panel.Controls.Add(reasonLabel, 0, reasonRow);
        _panel.SetColumnSpan(reasonLabel, 2);
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
            "Preview Next Repair",
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
