namespace PathVeer.Tray;

internal sealed class SaveFileDialogAdapter :
    ISupportBundleDialog
{
    public SupportBundleRequestResult? Prompt(
        SupportBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using SaveFileDialog dialog = new()
        {
            Title = "Save PathVeer Support Bundle",
            FileName = request.DefaultFileName,
            DefaultExt = "zip",
            Filter =
                "PathVeer Support Bundle (*.zip)|*.zip|" +
                "All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
            RestoreDirectory = false,
            InitialDirectory = GetDefaultDirectory()
        };

        DialogResult result = dialog.ShowDialog();

        if (result != DialogResult.OK)
        {
            return new SupportBundleRequestResult(
                ChosePath: false,
                Path: null);
        }

        return new SupportBundleRequestResult(
            ChosePath: true,
            Path: dialog.FileName);
    }

    private static string? GetDefaultDirectory()
    {
        try
        {
            string documents = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents))
            {
                return documents;
            }
        }
        catch
        {
        }

        return null;
    }
}

internal sealed class MessageBoxStatusSink :
    ISupportBundleStatusSink
{
    public void ShowSuccess(
        SupportBundleExportOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        string body =
            "Support bundle created.\n\nLocation:\n"
            + (outcome.OutputPath ?? "(unknown)");

        if (outcome.BytesWritten is { } bytes)
        {
            body +=
                $"\n\nSize: {bytes} bytes";
        }

        MessageBox.Show(
            body,
            "PathVeer support bundle",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public void ShowError(string message)
    {
        MessageBox.Show(
            message,
            "PathVeer support bundle",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
