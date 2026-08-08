using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Ipc;

namespace PathVeer.Tray;

public sealed class CustomRouteDialog : Form
{
    private readonly ICustomRouteCommandSender _sender;

    private readonly DataGridView _grid;
    private readonly Button _addButton;
    private readonly Button _toggleButton;
    private readonly Button _removeButton;
    private readonly Button _invalidateButton;
    private readonly Button _invalidateAllButton;
    private readonly Button _closeButton;
    private readonly Label _statusLabel;

    private IReadOnlyList<CustomRouteEntry> _entries = [];
    private IReadOnlyList<CustomRouteDnsCacheStatus> _statuses = [];
    private bool _busy;

    public CustomRouteDialog(
        ICustomRouteCommandSender? sender = null)
    {
        _sender = sender ?? new PathVeerServiceClient();

        Text = "Custom Routes";
        ClientSize = new Size(880, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _grid = new DataGridView
        {
            Location = new Point(12, 12),
            Size = new Size(856, 300),
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
                FillWeight = 6
            };

        DataGridViewTextBoxColumn typeColumn = new()
        {
            HeaderText = "Type",
            FillWeight = 11
        };

        DataGridViewTextBoxColumn valueColumn = new()
        {
            HeaderText = "Value",
            FillWeight = 26
        };

        DataGridViewTextBoxColumn cacheStateColumn = new()
        {
            HeaderText = "Cache State",
            FillWeight = 11
        };

        DataGridViewTextBoxColumn addressesColumn = new()
        {
            HeaderText = "Addresses",
            FillWeight = 20
        };

        DataGridViewTextBoxColumn expiresColumn = new()
        {
            HeaderText = "Expires",
            FillWeight = 11
        };

        DataGridViewTextBoxColumn descriptionColumn = new()
        {
            HeaderText = "Description",
            FillWeight = 15
        };

        _grid.Columns.Add(enabledColumn);
        _grid.Columns.Add(typeColumn);
        _grid.Columns.Add(valueColumn);
        _grid.Columns.Add(cacheStateColumn);
        _grid.Columns.Add(addressesColumn);
        _grid.Columns.Add(expiresColumn);
        _grid.Columns.Add(descriptionColumn);

        _grid.SelectionChanged += (_, _) =>
            UpdateButtons();

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

        _invalidateButton = new Button
        {
            Text = "Invalidate Cache",
            Location = new Point(300, 328),
            Size = new Size(90, 28)
        };

        _invalidateAllButton = new Button
        {
            Text = "Invalidate All",
            Location = new Point(396, 328),
            Size = new Size(90, 28)
        };

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(492, 328),
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

        _invalidateButton.Click += async (_, _) =>
            await InvalidateSelectedCacheAsync();

        _invalidateAllButton.Click += async (_, _) =>
            await InvalidateAllCachesAsync();

        _closeButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(_grid);
        Controls.Add(_addButton);
        Controls.Add(_toggleButton);
        Controls.Add(_removeButton);
        Controls.Add(_invalidateButton);
        Controls.Add(_invalidateAllButton);
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
            ServiceResponse listResponse =
                await _sender.SendAsync(
                    PathVeerCommand.CustomRoutesList);

            if (!listResponse.Success)
            {
                ShowError(listResponse.Message);
                _statusLabel.Text = "Failed to load routes.";
                return;
            }

            ServiceResponse statusResponse =
                await _sender.SendAsync(
                    PathVeerCommand.CustomRoutesCacheStatus);

            if (!statusResponse.Success)
            {
                ShowError(statusResponse.Message);
                _statusLabel.Text = "Failed to load routes.";
                return;
            }

            _entries = listResponse.CustomRoutes;
            _statuses =
                statusResponse.CustomRouteDnsCacheStatuses;
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
            CustomRouteDialogModel.MapRows(
                _entries,
                _statuses);

        _grid.Rows.Clear();

        foreach (CustomRouteListRow row in rows)
        {
            _grid.Rows.Add(
                row.Enabled,
                CustomRouteDialogModel.GetTypeLabel(row.Type),
                row.Value,
                row.CacheState,
                row.Addresses,
                row.Expires,
                row.Description ?? "");
        }

        _grid.ClearSelection();
        UpdateButtons();
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
            _entries,
            _statuses)[_grid.CurrentRow.Index];
    }

    private void UpdateButtons()
    {
        CustomRouteListRow? row = SelectedRow();
        _toggleButton.Text = row?.Enabled == true
            ? "Disable"
            : "Enable";
        _toggleButton.Enabled =
            !_busy && row is not null;
        _removeButton.Enabled =
            !_busy && row is not null;
        _invalidateButton.Enabled =
            !_busy &&
            CustomRouteDialogModel.CanInvalidateCache(row);
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

        PathVeerCommand command =
            row.Enabled
                ? PathVeerCommand.CustomRoutesDisable
                : PathVeerCommand.CustomRoutesEnable;

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
            PathVeerCommand.CustomRoutesRemove,
            row.Id.ToString());
    }

    private async Task InvalidateSelectedCacheAsync()
    {
        CustomRouteListRow? row = SelectedRow();

        if (!CustomRouteDialogModel.CanInvalidateCache(row))
        {
            return;
        }

        await InvalidateAsync(
            CustomRouteDialogModel.GetInvalidateCommand(
                all: false),
            row!.Id.ToString());
    }

    private async Task InvalidateAllCachesAsync()
    {
        DialogResult confirm = MessageBox.Show(
            this,
            CustomRouteDialogModel.InvalidateAllConfirmationMessage,
            "Custom Routes",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        await InvalidateAsync(
            CustomRouteDialogModel.GetInvalidateCommand(
                all: true),
            value: null);
    }

    private async Task InvalidateAsync(
        PathVeerCommand command,
        string? value)
    {
        SetBusy(true);
        _statusLabel.Text = "Invalidating...";

        try
        {
            ServiceResponse response =
                await _sender.SendAsync(command, value);

            if (!response.Success)
            {
                ShowError(response.Message);
                _statusLabel.Text = "Invalidation failed.";
                return;
            }

            _statusLabel.Text = response.Message;
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _statusLabel.Text = "Invalidation failed.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SendAsync(
        PathVeerCommand command,
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
        _invalidateAllButton.Enabled = !busy;
        _grid.Enabled = !busy;

        UpdateButtons();
    }
}
