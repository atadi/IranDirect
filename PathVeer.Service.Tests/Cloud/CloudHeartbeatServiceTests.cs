using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PathVeer.Core.Cloud;
using PathVeer.TestSupport.Http;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

public sealed class CloudHeartbeatServiceTests
{
    private const string BaseUrl = "https://cloud.pathveer.test";

    private static PathVeerCloudClient ClientWith(
        StubHttpMessageHandler stub,
        TimeSpan? timeout = null) =>
        new(
            BaseUrl,
            stub,
            timeout ?? TimeSpan.FromSeconds(10));

    // P. Cloud outage must not fail Service startup.
    [Fact]
    public async Task ExecuteAsync_UnconfiguredBaseUrl_ReturnsImmediately()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            CloudOptions options = new(); // empty BaseUrl
            CloudHeartbeatService svc = new(
                store, ClientWith(new StubHttpMessageHandler(_ =>
                    StubHttpMessageHandler.NoContent(HttpStatusCode.OK))),
                options,
                NullLogger<CloudHeartbeatService>.Instance);

            // With no BaseUrl the worker idles; awaiting a short while must
            // not throw or block.
            Task task = svc.StartAsync(CancellationToken.None);
            await Task.Delay(20);
            await svc.StopAsync(CancellationToken.None);
            await task;
        }
        finally
        {
            File.Delete(path);
        }
    }

    // J + I. successful heartbeat updates local metadata and sends Bearer.
    [Fact]
    public async Task BeatOnceAsync_Success_UpdatesLastHeartbeat()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "device-1", "org-1", "cred", null,
                DateTimeOffset.UtcNow);

            StubHttpMessageHandler stub = new(_ =>
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    "{\"deviceId\":\"device-1\",\"organizationId\":\"org-1\"," +
                    "\"status\":\"active\",\"serverTime\":\"2026-01-01T00:00:00Z\"}"));
            CloudHeartbeatService svc = new(
                store, ClientWith(stub),
                new CloudOptions { BaseUrl = BaseUrl },
                NullLogger<CloudHeartbeatService>.Instance);

            await svc.BeatOnceAsync(CancellationToken.None);

            Assert.Equal("Bearer cred", stub.LastAuthorizationHeader);
            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.Connected, view.State);
            Assert.NotNull(view.LastHeartbeatUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // K. revoked heartbeat moves local Cloud state to revoked/re-enroll.
    [Fact]
    public async Task BeatOnceAsync_Revoked_ClearsCredentialAndMarksRevoked()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "device-1", "org-1", "cred", null,
                DateTimeOffset.UtcNow);

            StubHttpMessageHandler stub = new(_ =>
                StubHttpMessageHandler.Json(
                    HttpStatusCode.Unauthorized,
                    "{\"error\":\"Device credential has been revoked.\"}"));
            CloudHeartbeatService svc = new(
                store, ClientWith(stub),
                new CloudOptions { BaseUrl = BaseUrl },
                NullLogger<CloudHeartbeatService>.Instance);

            await svc.BeatOnceAsync(CancellationToken.None);

            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.Revoked, view.State);
            Assert.False(view.HasCredential);

            // A second beat must not touch the store again (no storm).
            await svc.BeatOnceAsync(CancellationToken.None);
            CloudRegistrationView after = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.Revoked, after.State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // O. un-enrolled installation sends no authenticated heartbeat.
    [Fact]
    public async Task BeatOnceAsync_NotEnrolled_DoesNothing()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            StubHttpMessageHandler stub = new(_ =>
                throw new InvalidOperationException("must not be called"));
            CloudHeartbeatService svc = new(
                store, ClientWith(stub),
                new CloudOptions { BaseUrl = BaseUrl },
                NullLogger<CloudHeartbeatService>.Instance);

            // Must not throw and must not hit the network.
            await svc.BeatOnceAsync(CancellationToken.None);

            Assert.Null(stub.LastRequest);
            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.NotConnected, view.State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // M. machine-identifying telemetry is absent from heartbeat.
    [Fact]
    public async Task BeatOnceAsync_NoMachineTelemetry()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "device-1", "org-1", "cred", null,
                DateTimeOffset.UtcNow);

            StubHttpMessageHandler stub = new(_ =>
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    "{\"deviceId\":\"device-1\",\"organizationId\":\"org-1\"," +
                    "\"status\":\"active\",\"serverTime\":\"2026-01-01T00:00:00Z\"}"));
            CloudHeartbeatService svc = new(
                store, ClientWith(stub),
                new CloudOptions { BaseUrl = BaseUrl },
                NullLogger<CloudHeartbeatService>.Instance);

            await svc.BeatOnceAsync(CancellationToken.None);

            string? body = stub.LastRequestBody;
            Assert.NotNull(body);
            foreach (string banned in new[]
                     {
                         "hostname", "machineguid", "mac", "ip",
                         "route", "wireguard", "username", "machine",
                         "serial", "hardware", "cpu", "disk"
                     })
            {
                Assert.DoesNotContain(
                    banned,
                    body!,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempFile() =>
        Path.Combine(
            Path.GetTempPath(),
            "pvcloud-hb-" + Guid.NewGuid().ToString("N") + ".json");
}
