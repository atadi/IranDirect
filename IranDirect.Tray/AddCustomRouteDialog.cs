using IranDirect.Core.CustomRoutes;

namespace IranDirect.Tray;

public sealed class AddCustomRouteDialog : Form
{
    private readonly ComboBox _typeCombo;
    private readonly TextBox _valueBox;
    private readonly TextBox _descriptionBox;
    private readonly Label _valueHintLabel;

    public CustomRouteEntryType SelectedType { get; private set; } =
        CustomRouteEntryType.Domain;

    public string? Value { get; private set; }

    public string? Description { get; private set; }

    public AddCustomRouteDialog()
    {
        Text = "Add Custom Route";
        ClientSize = new Size(360, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        Label typeLabel = new()
        {
            Text = "Type:",
            Location = new Point(12, 15),
            Size = new Size(80, 20)
        };

        _typeCombo = new ComboBox
        {
            Location = new Point(100, 12),
            Size = new Size(240, 25),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        _valueHintLabel = new Label
        {
            Text = $"Value ({GetValueHintText(SelectedType)}):",
            Location = new Point(12, 55),
            Size = new Size(80, 20)
        };

        foreach (string label in CustomRouteDialogModel.TypeLabels)
        {
            _typeCombo.Items.Add(label);
        }

        _typeCombo.SelectedIndexChanged += (_, _) =>
        {
            SelectedType =
                CustomRouteDialogModel.TryGetType(
                    _typeCombo.SelectedItem?.ToString() ?? "",
                    out CustomRouteEntryType type)
                    ? type
                    : CustomRouteEntryType.Domain;

            _valueHintLabel.Text =
                $"Value ({GetValueHintText(SelectedType)}):";
        };

        _typeCombo.SelectedIndex = 0;

        _valueBox = new TextBox
        {
            Location = new Point(100, 52),
            Size = new Size(240, 23)
        };

        Label descriptionLabel = new()
        {
            Text = "Description:",
            Location = new Point(12, 95),
            Size = new Size(80, 20)
        };

        _descriptionBox = new TextBox
        {
            Location = new Point(100, 92),
            Size = new Size(240, 23)
        };

        Button saveButton = new()
        {
            Text = "Save",
            DialogResult = DialogResult.None,
            Location = new Point(170, 145),
            Size = new Size(80, 25)
        };

        Button cancelButton = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(260, 145),
            Size = new Size(80, 25)
        };

        saveButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_valueBox.Text))
            {
                MessageBox.Show(
                    "A value is required.",
                    "Add Custom Route",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SelectedType =
                CustomRouteDialogModel.TryGetType(
                    _typeCombo.SelectedItem?.ToString() ?? "",
                    out CustomRouteEntryType type)
                    ? type
                    : CustomRouteEntryType.Domain;

            Value = _valueBox.Text.Trim();
            Description =
                string.IsNullOrWhiteSpace(_descriptionBox.Text)
                    ? null
                    : _descriptionBox.Text.Trim();

            DialogResult = DialogResult.OK;
            Close();
        };

        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.Add(typeLabel);
        Controls.Add(_typeCombo);
        Controls.Add(_valueHintLabel);
        Controls.Add(_valueBox);
        Controls.Add(descriptionLabel);
        Controls.Add(_descriptionBox);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);
    }

    private static string GetValueHintText(
        CustomRouteEntryType type)
    {
        return type switch
        {
            CustomRouteEntryType.Domain => "domain",
            CustomRouteEntryType.IpAddress => "IPv4 address",
            CustomRouteEntryType.Cidr => "CIDR",
            _ => "value"
        };
    }
}
