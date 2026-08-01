using System.Text;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Ipc;

namespace IranDirect.Tray;

public sealed class CustomRouteDialog : Form
{
    private readonly ICustomRouteCommandSender _sender;

    private readonly DataGridView _grid;
    private readonly Button _addButton;
    private readonly Button _toggleButton;
    private readonly Button _removeButton;
    private readonly Button _resolveButton;
    private readonly Button _closeButton;
    private readonly Label _statusLabel;

    private IReadOnlyList<CustomRouteEntry> _entries = [];
    private bool _busy;

    public CustomRouteDialog(
        ICustomRouteCommandSender? sender = null)
    {
        _sender = sender ?? new IranDirectServiceClient();

        Text = "Custom Routes";
        ClientSize = new Size(640, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _grid = new DataGridView
        {
            Location = new Point(12, 12),
            Size = new Size(616, 300),
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            SelectionMode =
                DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            AutoSizeColumnsMode =
                DataGridViewAutoSizeColumnsMode.Fill
        };

        DataGridViewCheckBoxColumn enabledColumn =
            new()
            {
                HeaderText = "Enabled",
                FillWeight = 12
            };

        DataGridViewTextBoxColumn typeColumn = new()
        {
            HeaderText = "Type",
            FillWeight = 14
        };

        DataGridViewTextBoxColumn valueColumn = new()
        {
            HeaderText = "Value",
            FillWeight = 40
        };

        DataGridViewTextBoxColumn descriptionColumn = new()
        {
            HeaderText = "Description",
            FillWeight = 34
        };

        _grid.Columns.Add(enabledColumn);
        _grid.Columns.Add(typeColumn);
        _grid.Columns.Add(valueColumn);
        _grid.Columns.Add(descriptionColumn);

        _grid.SelectionChanged += (_, _) =>
            UpdateToggleButton();

        _addButton = new Button
        {
            Text = "Add...",
            Location = new Point(12, 328),
            Size = new Size(90, 28)
        };

        _toggleButton = new Button
        {
            Text = "Enable",
            Location = new Point(108, 328),
            Size = new Size(90, 28)
        };

        _removeButton = new Button
        {
            Text = "Remove",
            Location = new Point(204, 328),
            Size = new Size(90, 28)
        };

        _resolveButton = new Button
        {
            Text = "Resolve Now",
            Location = new Point(300, 328),
            Size = new Size(90, 28)
        };

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(538, 328),
            Size = new Size(90, 28)
        };

        _statusLabel = new Label
        {
            Location = new Point(12, 370),
            Size = new Size(616, 20),
            Text = "Loading..."
        };

        _addButton.Click += async (_, _) =>
            await AddAsync();

        _toggleButton.Click += async (_, _) =>
            await ToggleSelectedAsync();

        _removeButton.Click += async (_, _) =>
            await RemoveSelectedAsync();

        _resolveButton.Click += async (_, _) =>
            await ResolveAsync();

        _closeButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(_grid);
        Controls.Add(_addButton);
        Controls.Add(_toggleButton);
        Controls.Add(_removeButton);
        Controls.Add(_resolveButton);
        Controls.Add(_closeButton);
        Controls.Add(_statusLabel);

        Shown += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        _statusLabel.Text = "Loading...";

        try
        {
            ServiceResponse response =
                await _sender.SendAsync(
                    IranDirectCommand.CustomRoutesList);

            if (!response.Success)
            {
                ShowError(response.Message);
                _statusLabel.Text = "Failed to load routes.";
                return;
            }

            _entries = response.CustomRoutes;
            BindGrid();
            _statusLabel.Text =
                $"{_entries.Count} custom route(s).";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _statusLabel.Text = "Failed to load routes.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BindGrid()
    {
        IReadOnlyList<CustomRouteListRow> rows =
            CustomRouteDialogModel.MapRows(_entries);

        _grid.Rows.Clear();

        foreach (CustomRouteListRow row in rows)
        {
            _grid.Rows.Add(
                row.Enabled,
                CustomRouteDialogModel.GetTypeLabel(row.Type),
                row.Value,
                row.Description ?? "");
        }

        _grid.ClearSelection();
        UpdateToggleButton();
    }

    private CustomRouteListRow? SelectedRow()
    {
        if (_grid.CurrentRow is null ||
            _grid.CurrentRow.Index < 0 ||
            _grid.CurrentRow.Index >= _entries.Count)
        {
            return null;
        }

        return CustomRouteDialogModel.MapRows(
            _entries)[_grid.CurrentRow.Index];
    }

    private void UpdateToggleButton()
    {
        CustomRouteListRow? row = SelectedRow();
        _toggleButton.Text = row?.Enabled == true
            ? "Disable"
            : "Enable";
        _toggleButton.Enabled =
            !_busy && row is not null;
        _removeButton.Enabled =
            !_busy && row is not null;
    }

    private async Task AddAsync()
    {
        using AddCustomRouteDialog dialog = new();

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await SendAsync(
            CustomRouteDialogModel.GetAddCommand(
                dialog.SelectedType),
            dialog.Value,
            dialog.Description);
    }

    private async Task ToggleSelectedAsync()
    {
        CustomRouteListRow? row = SelectedRow();

        if (row is null)
        {
            return;
        }

        IranDirectCommand command =
            row.Enabled
                ? IranDirectCommand.CustomRoutesDisable
                : IranDirectCommand.CustomRoutesEnable;

        await SendAsync(command, row.Id.ToString());
    }

    private async Task RemoveSelectedAsync()
    {
        CustomRouteListRow? row = SelectedRow();

        if (row is null)
        {
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            $"Remove custom route '{row.Value}'?",
            "Custom Routes",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        await SendAsync(
            IranDirectCommand.CustomRoutesRemove,
            row.Id.ToString());
    }

    private async Task ResolveAsync()
    {
        SetBusy(true);
        _statusLabel.Text = "Resolving...";

        try
        {
            ServiceResponse response =
                await _sender.SendAsync(
                    IranDirectCommand.CustomRoutesResolve);

            if (!response.Success ||
                response.CustomRouteResolution is null)
            {
                ShowError(response.Message);
                _statusLabel.Text = "Resolution failed.";
                return;
            }

            CustomRouteResolutionResult result =
                response.CustomRouteResolution;

            _statusLabel.Text =
                CustomRouteDialogModel.BuildResolutionSummary(
                    result);

            MessageBox.Show(
                this,
                BuildResolutionText(result),
                "Custom Routes — Resolution",
                MessageBoxButtons.OK,
                result.Failures.Count == 0
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _statusLabel.Text = "Resolution failed.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SendAsync(
        IranDirectCommand command,
        string? value,
        string? description = null)
    {
        SetBusy(true);
        _statusLabel.Text = "Working...";

        try
        {
            ServiceResponse response =
                await _sender.SendAsync(
                    command,
                    value,
                    description);

            if (!response.Success)
            {
                ShowError(response.Message);
                _statusLabel.Text = "Operation failed.";
                return;
            }

            _entries = response.CustomRoutes;
            BindGrid();
            _statusLabel.Text = response.Message;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _statusLabel.Text = "Operation failed.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string BuildResolutionText(
        CustomRouteResolutionResult result)
    {
        StringBuilder text = new();

        text.Append(
            $"Resolved prefixes: {result.Prefixes.Count}\n");

        foreach (string prefix in result.Prefixes)
        {
            text.AppendLine(prefix);
        }

        text.AppendLine();

        if (result.Failures.Count == 0)
        {
            text.Append("Failures: none");
            return text.ToString();
        }

        text.Append($"Failures: {result.Failures.Count}\n");

        foreach (CustomRouteResolutionFailure failure
                 in result.Failures)
        {
            text.AppendLine(
                $"- [{CustomRouteDialogModel.GetTypeLabel(
                    failure.Type)}] {failure.Value}: " +
                failure.Reason);
        }

        return text.ToString();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Custom Routes",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;

        _addButton.Enabled = !busy;
        _resolveButton.Enabled = !busy;
        _grid.Enabled = !busy;

        UpdateToggleButton();
    }
}
