using System.Net;
using System.Text;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class OfficialIranPrefixSourceFaultInjectionTests
{
    private const string ValidPayload = """
        {
          "data": {
            "resources": {
              "ipv4": [ "2.2.2.0/24", "5.5.5.0/24" ]
            }
          }
        }
        """;

    [Fact]
    public async Task FetchAsync_InjectedHttpFault_ThrowsCorrectException()
    {
        RecordingHandler handler = new();
        OfficialIranPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => source.FetchAsync(
                    new PrefixSourceRequest()));

        Assert.Equal(
            FaultInjectionPoint.HttpRequest,
            exception.Point);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_InjectedHttpFault_ZeroHandlerRequests()
    {
        RecordingHandler handler = new();
        OfficialIranPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => source.FetchAsync(new PrefixSourceRequest()));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_InjectedHttpFault_IsNotRetried()
    {
        RecordingHandler handler = new();
        OfficialIranPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => source.FetchAsync(new PrefixSourceRequest()));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_NextCall_SucceedsAfterFault()
    {
        RecordingHandler handler = new(ValidPayload);
        OfficialIranPrefixSource faulted = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faulted.FetchAsync(new PrefixSourceRequest()));

        OfficialIranPrefixSource source = CreateSource(handler);

        PrefixSourceFetchResult result =
            await source.FetchAsync(new PrefixSourceRequest());

        Assert.Equal(2, result.Prefixes.Count);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_NestedScope_ActivatesOnlySelectedPoint()
    {
        RecordingHandler handler = new(ValidPayload);

        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(FaultInjectionPoint.HttpRequest))
        {
            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(FaultInjectionPoint.FileRead))
            {
                OfficialIranPrefixSource nestedSource =
                    CreateSource(handler);

                PrefixSourceFetchResult result =
                    await nestedSource.FetchAsync(
                        new PrefixSourceRequest());

                Assert.Equal(2, result.Prefixes.Count);
                Assert.Equal(1, handler.RequestCount);
            }

            OfficialIranPrefixSource source =
                CreateSource(handler);

            FaultInjectionException exception =
                await Assert.ThrowsAsync<FaultInjectionException>(
                    () => source.FetchAsync(
                        new PrefixSourceRequest()));

            Assert.Equal(
                FaultInjectionPoint.HttpRequest,
                exception.Point);
            Assert.Equal(1, handler.RequestCount);
        }
    }

    [Fact]
    public async Task FetchAsync_InjectedPolicy_WorksWithoutAmbientScope()
    {
        RecordingHandler handler = new();
        OfficialIranPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        Assert.False(FaultInjectionScope.IsActive);

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => source.FetchAsync(new PrefixSourceRequest()));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_AmbientScope_TakesPrecedenceOverInjectedPolicy()
    {
        RecordingHandler handler = new();
        OfficialIranPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileRead]));

        using (FaultInjectionScope.Fail(FaultInjectionPoint.HttpRequest))
        {
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => source.FetchAsync(new PrefixSourceRequest()));
        }

        Assert.Equal(0, handler.RequestCount);
    }

    private static OfficialIranPrefixSource CreateSource(
        RecordingHandler handler,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        return new OfficialIranPrefixSource(
            new HttpClient(handler),
            faultPolicy: faultPolicy);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string? _payload;

        public RecordingHandler(string? payload = null)
        {
            _payload = payload;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            if (_payload is null)
            {
                throw new InvalidOperationException(
                    "Unexpected request during fault injection.");
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _payload,
                    Encoding.UTF8,
                    "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
