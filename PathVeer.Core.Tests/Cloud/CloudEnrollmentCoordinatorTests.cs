using System.Net;
using System.Net.Http;
using PathVeer.Core.Cloud;
using PathVeer.Core.Installation;
using PathVeer.TestSupport.Http;
using Xunit;

namespace PathVeer.Core.Tests.Cloud;

public sealed class CloudEnrollmentCoordinatorTests
{
    private const string BaseUrl = "https://cloud.pathveer.test";

    private static PathVeerCloudClient ClientWith(
        StubHttpMessageHandler stub,
        TimeSpan? timeout = null) =>
        new(
            BaseUrl,
            stub,
            timeout ?? TimeSpan.FromSeconds(10));

    // G. existing enrollment is not silently overwritten.
    [Fact]
    public async Task EnrollAsync_WhenAlreadyEnrolled_RejectsWithoutForce()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "existing-device", "existing-org", "old-cred", null,
                DateTimeOffset.UtcNow);

            StubHttpMessageHandler stub = new(_ =>
                StubHttpMessageHandler.Json(
                    HttpStatusCode.Created,
                    "{\"deviceId\":\"new-device\",\"organizationId\"" +
                    ":\"new-org\",\"credential\":\"new-cred\"}"));
            CloudEnrollmentCoordinator coordinator = new(
                ClientWith(stub),
                store);

            CloudEnrollmentResult result = await coordinator.EnrollAsync(
                "X", "label", force: false);

            Assert.False(result.Success);
            // Local identity is unchanged.
            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal("existing-device", view.DeviceId);
            Assert.Equal("existing-org", view.OrganizationId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EnrollAsync_WithForce_ReplacesExistingRegistration()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "existing-device", "existing-org", "old-cred", null,
                DateTimeOffset.UtcNow);

            StubHttpMessageHandler stub = new(_ =>
                StubHttpMessageHandler.Json(
                    HttpStatusCode.Created,
                    "{\"deviceId\":\"new-device\",\"organizationId\"" +
                    ":\"new-org\",\"credential\":\"new-cred\"}"));
            CloudEnrollmentCoordinator coordinator = new(
                ClientWith(stub),
                store);

            CloudEnrollmentResult result = await coordinator.EnrollAsync(
                "X", "label", force: true);

            Assert.True(result.Success);
            Assert.Equal("new-device", result.DeviceId);

            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal("new-device", view.DeviceId);
            Assert.Equal("new-org", view.OrganizationId);
            string? cred = await store.GetCredentialAsync();
            Assert.Equal("new-cred", cred);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // F. HTTP/network failure leaves local PathVeer operational.
    // The coordinator must NOT create any local identity on network failure.
    [Fact]
    public async Task EnrollAsync_NetworkFailure_NoLocalIdentity()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());

            // Simulate transport failure (throwing handler).
            StubHttpMessageHandler stub = new(_ =>
                throw new HttpRequestException("no route"));

            CloudEnrollmentCoordinator coordinator = new(
                ClientWith(stub),
                store);

            CloudEnrollmentResult result = await coordinator.EnrollAsync(
                "X", "label", force: false);

            Assert.False(result.Success);

            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.NotConnected, view.State);
            Assert.False(view.HasCredential);
            Assert.Null(view.DeviceId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Fail-closed: Cloud accepts the code but persistence fails -> the
    // enrollment is reported failed (no silent second identity). The store
    // remains empty / unchanged.
    [Fact]
    public async Task EnrollAsync_PersistenceFails_NoSilentSuccess()
    {
        // Make the target's parent an existing FILE so even creating the
        // directory (and thus the atomic tmp+move) fails. The store must throw
        // and the coordinator must report failure without any local identity.
        string existingFile = Path.Combine(
            Path.GetTempPath(),
            "pvcloud-ro-" + Guid.NewGuid().ToString("N") + ".lock");
        await File.WriteAllTextAsync(existingFile, "lock");
        string path = Path.Combine(existingFile, "cloud-registration.json");

        StubHttpMessageHandler stub = new(_ =>
            StubHttpMessageHandler.Json(
                HttpStatusCode.Created,
                "{\"deviceId\":\"d1\",\"organizationId\":\"o1\"," +
                "\"credential\":\"c1\"}"));
        CloudRegistrationStore store =
            new(path, new InMemoryCloudSecretProtector());
        CloudEnrollmentCoordinator coordinator = new(
            ClientWith(stub),
            store);

        CloudEnrollmentResult result = await coordinator.EnrollAsync(
            "X", "label", force: false);

        Assert.False(result.Success);
        // No identity was created locally despite Cloud "accepting" the code.
        CloudRegistrationView view = await store.GetViewAsync();
        Assert.Equal(CloudConnectionState.NotConnected, view.State);
        Assert.Null(view.DeviceId);
        Assert.False(view.HasCredential);
    }

    // Adversarial: a malformed/hostile Cloud enrollment response that tries to
    // smuggle unexpected fields must NOT be trusted and must fail closed.
    [Fact]
    public async Task EnrollAsync_HostileResponse_FailsClosed()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());

            // Response with extra/garbage fields and missing required ones.
            StubHttpMessageHandler stub = new(_ =>
                StubHttpMessageHandler.Json(
                    HttpStatusCode.Created,
                    "{\"deviceId\":null,\"organizationId\":null," +
                    "\"credential\":null,\"__evil\":\"drop table\"}"));
            CloudEnrollmentCoordinator coordinator = new(
                ClientWith(stub),
                store);

            CloudEnrollmentResult result = await coordinator.EnrollAsync(
                "X", "label", force: false);

            Assert.False(result.Success);
            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.NotConnected, view.State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempFile() =>
        Path.Combine(
            Path.GetTempPath(),
            "pvcloud-coord-" + Guid.NewGuid().ToString("N") + ".json");
}
