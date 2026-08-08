using System.Net;
using System.Text;
using PathVeer.Core.Configuration;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Prefixes;

public sealed class OfficialCountryPrefixSourceFaultInjectionTests
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
        OfficialCountryPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => source.FetchAsync(
                    DirectCountryCode.IR));

        Assert.Equal(
            FaultInjectionPoint.HttpRequest,
            exception.Point);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_InjectedHttpFault_ZeroHandlerRequests()
    {
        RecordingHandler handler = new();
        OfficialCountryPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => source.FetchAsync(DirectCountryCode.IR));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_InjectedHttpFault_IsNotRetried()
    {
        RecordingHandler handler = new();
        OfficialCountryPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => source.FetchAsync(DirectCountryCode.IR));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_NextCall_SucceedsAfterFault()
    {
        RecordingHandler handler = new(ValidPayload);
        OfficialCountryPrefixSource faulted = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faulted.FetchAsync(DirectCountryCode.IR));

        OfficialCountryPrefixSource source = CreateSource(handler);

        PrefixSourceFetchResult result =
            await source.FetchAsync(DirectCountryCode.IR);

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
                OfficialCountryPrefixSource nestedSource =
                    CreateSource(handler);

                PrefixSourceFetchResult result =
                    await nestedSource.FetchAsync(
                        DirectCountryCode.IR);

                Assert.Equal(2, result.Prefixes.Count);
                Assert.Equal(1, handler.RequestCount);
            }

            OfficialCountryPrefixSource source =
                CreateSource(handler);

            FaultInjectionException exception =
                await Assert.ThrowsAsync<FaultInjectionException>(
                    () => source.FetchAsync(
                        DirectCountryCode.IR));

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
        OfficialCountryPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        Assert.False(FaultInjectionScope.IsActive);

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => source.FetchAsync(DirectCountryCode.IR));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_AmbientScope_TakesPrecedenceOverInjectedPolicy()
    {
        RecordingHandler handler = new();
        OfficialCountryPrefixSource source = CreateSource(
            handler,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileRead]));

        using (FaultInjectionScope.Fail(FaultInjectionPoint.HttpRequest))
        {
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => source.FetchAsync(DirectCountryCode.IR));
        }

        Assert.Equal(0, handler.RequestCount);
    }

    private static OfficialCountryPrefixSource CreateSource(
        RecordingHandler handler,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        return new OfficialCountryPrefixSource(
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
