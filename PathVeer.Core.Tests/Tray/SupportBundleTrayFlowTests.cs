using PathVeer.Core.Ipc;
using PathVeer.Tray;

namespace PathVeer.Core.Tests.Tray;

public sealed class SupportBundleTrayFlowTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportAsync_DispatchesSupportBundleExportCommand()
    {
        RecordingSender sender = new();
        StubDialog dialog = new(
            new SupportBundleRequestResult(
                ChosePath: true,
                Path: "C:\\bundle.zip"));

        await SupportBundleTrayFlow.ExportAsync(
            sender,
            dialog,
            new RecordingStatusSink(),
            new FixedTimeProvider(FixedTime));

        Assert.Equal(
            [IranDirectCommand.SupportBundleExport],
            sender.Commands);
        Assert.Equal(
            "C:\\bundle.zip",
            sender.LastValue);
    }

    [Fact]
    public async Task ExportAsync_SenderInvokedExactlyOnce()
    {
        RecordingSender sender = new();
        StubDialog dialog = new(
            new SupportBundleRequestResult(
                ChosePath: true,
                Path: "C:\\bundle.zip"));

        await SupportBundleTrayFlow.ExportAsync(
            sender,
            dialog,
            new RecordingStatusSink(),
            new FixedTimeProvider(FixedTime));

        Assert.Equal(1, sender.CallCount);
    }

    [Fact]
    public async Task ExportAsync_SaveCancelled_NoExportAttempted()
    {
        RecordingSender sender = new();
        StubDialog dialog = new(
            new SupportBundleRequestResult(
                ChosePath: false,
                Path: null));
        RecordingStatusSink sink = new();

        await SupportBundleTrayFlow.ExportAsync(
            sender,
            dialog,
            sink,
            new FixedTimeProvider(FixedTime));

        Assert.Equal(0, sender.CallCount);
        Assert.Empty(sink.Successes);
        Assert.Empty(sink.Errors);
    }

    [Fact]
    public async Task ExportAsync_SuccessfulResponse_ShowsSuccess()
    {
        RecordingSender sender = new(
            success: true,
            message: "Support bundle written: C:\\bundle.zip (1234 bytes).",
            bundlePath: "C:\\bundle.zip",
            bytesWritten: 1234);

        StubDialog dialog = new(
            new SupportBundleRequestResult(
                ChosePath: true,
                Path: "C:\\bundle.zip"));

        RecordingStatusSink sink = new();

        await SupportBundleTrayFlow.ExportAsync(
            sender,
            dialog,
            sink,
            new FixedTimeProvider(FixedTime));

        SupportBundleExportOutcome outcome =
            Assert.Single(sink.Successes);

        Assert.Equal("C:\\bundle.zip", outcome.OutputPath);
        Assert.Equal(1234, outcome.BytesWritten);
        Assert.True(outcome.Success);
    }

    [Fact]
    public async Task ExportAsync_FailedResponse_ShowsError()
    {
        RecordingSender sender = new(
            success: false,
            message: "service unavailable");

        StubDialog dialog = new(
            new SupportBundleRequestResult(
                ChosePath: true,
                Path: "C:\\bundle.zip"));

        RecordingStatusSink sink = new();

        await SupportBundleTrayFlow.ExportAsync(
            sender,
            dialog,
            sink,
            new FixedTimeProvider(FixedTime));

        string error = Assert.Single(sink.Errors);
        Assert.Equal("service unavailable", error);
        Assert.Empty(sink.Successes);
    }

    [Fact]
    public async Task ExportAsync_ExceptionFromSender_ShowsError()
    {
        ThrowingSender sender = new(
            new TimeoutException("timed out"));

        StubDialog dialog = new(
            new SupportBundleRequestResult(
                ChosePath: true,
                Path: "C:\\bundle.zip"));

        RecordingStatusSink sink = new();

        await SupportBundleTrayFlow.ExportAsync(
            sender,
            dialog,
            sink,
            new FixedTimeProvider(FixedTime));

        string error = Assert.Single(sink.Errors);
        Assert.Equal("timed out", error);
    }

    [Fact]
    public async Task ExportAsync_NullArgs_Throw()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SupportBundleTrayFlow.ExportAsync(
                null!,
                new StubDialog(null),
                new RecordingStatusSink(),
                new FixedTimeProvider(FixedTime)));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SupportBundleTrayFlow.ExportAsync(
                new RecordingSender(),
                null!,
                new RecordingStatusSink(),
                new FixedTimeProvider(FixedTime)));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SupportBundleTrayFlow.ExportAsync(
                new RecordingSender(),
                new StubDialog(null),
                null!,
                new FixedTimeProvider(FixedTime)));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SupportBundleTrayFlow.ExportAsync(
                new RecordingSender(),
                new StubDialog(null),
                new RecordingStatusSink(),
                null!));
    }

    [Fact]
    public void Tray_DoesNotReferenceSnapshotProviderOrExporterDirectly()
    {
        System.Reflection.Assembly trayAssembly =
            typeof(SupportBundleMenuPolicy).Assembly;

        string[] forbiddenNames =
        [
            "SupportSnapshotProvider",
            "SupportSnapshotSerializer",
            "SupportSnapshotExporter",
            "SupportBundleExporter"
        ];

        foreach (string forbidden in forbiddenNames)
        {
            System.Type? referenced = trayAssembly.GetType(
                $"PathVeer.Tray.{forbidden}",
                throwOnError: false);

            Assert.Null(referenced);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class StubDialog : ISupportBundleDialog
    {
        private readonly SupportBundleRequestResult? _result;

        public StubDialog(SupportBundleRequestResult? result)
        {
            _result = result;
        }

        public SupportBundleRequestResult? Prompt(
            SupportBundleRequest request)
        {
            return _result;
        }
    }

    private sealed class RecordingStatusSink :
        ISupportBundleStatusSink
    {
        public List<SupportBundleExportOutcome>
            Successes { get; } = [];

        public List<string> Errors { get; } = [];

        public void ShowSuccess(
            SupportBundleExportOutcome outcome)
        {
            Successes.Add(outcome);
        }

        public void ShowError(string message)
        {
            Errors.Add(message);
        }
    }

    private sealed class RecordingSender :
        ICustomRouteCommandSender
    {
        private readonly bool _success;
        private readonly string _message;
        private readonly string? _bundlePath;
        private readonly long? _bytesWritten;

        public RecordingSender(
            bool success = true,
            string message = "ok",
            string? bundlePath = null,
            long? bytesWritten = null)
        {
            _success = success;
            _message = message;
            _bundlePath = bundlePath;
            _bytesWritten = bytesWritten;
        }

        public int CallCount { get; private set; }

        public List<IranDirectCommand> Commands { get; } = [];

        public string? LastValue { get; private set; }

        public Task<ServiceResponse> SendAsync(
            IranDirectCommand command,
            string? value = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Commands.Add(command);
            LastValue = value;
            return Task.FromResult(new ServiceResponse
            {
                Success = _success,
                Message = _message,
                SupportBundlePath = _bundlePath,
                SupportBundleBytesWritten = _bytesWritten
            });
        }
    }

    private sealed class ThrowingSender :
        ICustomRouteCommandSender
    {
        private readonly Exception _exception;

        public ThrowingSender(Exception exception)
        {
            _exception = exception;
        }

        public Task<ServiceResponse> SendAsync(
            IranDirectCommand command,
            string? value = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }
}
