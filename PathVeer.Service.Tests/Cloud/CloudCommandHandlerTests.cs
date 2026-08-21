using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PathVeer.Core.Cloud;
using PathVeer.Core.Ipc;
using PathVeer.TestSupport.Http;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// Exercises the IPC boundary: the Tray sends Cloud commands; the Service
/// performs the authoritative exchange. The Tray must NEVER receive the raw
/// credential and must not persist the enrollment code.
/// </summary>
public sealed class CloudCommandHandlerTests
{
    private const string BaseUrl = "https://cloud.pathveer.test";

    private static PathVeerCloudClient ClientWith(
        StubHttpMessageHandler stub) =>
        new(BaseUrl, stub, TimeSpan.FromSeconds(10));

    // A + D + B via IPC: enroll command persists identity, returns NO
    // credential, and the store does not persist the enrollment code.
    [Fact]
    public async Task Enroll_PersistsAndHidesCredential()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            StubHttpMessageHandler stub = new(_ =>
                StubHttpMessageHandler.Json(
                    HttpStatusCode.Created,
                    "{\"deviceId\":\"d1\",\"organizationId\":\"o1\"," +
                    "\"credential\":\"SUPER-SECRET\"}"));
            CloudEnrollmentCoordinator coordinator = new(
                ClientWith(stub), store);

            CloudCommandHandler handler = new(coordinator, store);

            ServiceResponse response = await handler.EnrollAsync(
                "ENROLL-CODE-XYZ", "Operator Laptop", force: false);

            Assert.True(response.Success);
            Assert.NotNull(response.CloudRegistration);
            Assert.Equal("d1", response.CloudRegistration!.DeviceId);
            Assert.Equal("o1", response.CloudRegistration.OrganizationId);
            // The Tray view type carries no raw-credential field: the credential
            // is structurally unavailable to the UI (see CloudRegistrationView).
            Assert.True(response.CloudRegistration.HasCredential);

            // The enrollment code is not persisted to disk, and the raw
            // credential is not persisted in plaintext (protected via the
            // secret protector).
            string onDisk = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("ENROLL-CODE-XYZ", onDisk);
            Assert.DoesNotContain("SUPER-SECRET", onDisk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // O + status: un-enrolled status returns NotConnected with no credential.
    [Fact]
    public async Task Status_Unenrolled_ReturnsNotConnected()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            CloudEnrollmentCoordinator coordinator = new(
                ClientWith(new StubHttpMessageHandler(_ =>
                    StubHttpMessageHandler.NoContent(HttpStatusCode.OK))),
                store);
            CloudCommandHandler handler = new(coordinator, store);

            ServiceResponse response = await handler.StatusAsync();

            Assert.True(response.Success);
            Assert.NotNull(response.CloudRegistration);
            Assert.Equal(
                CloudConnectionState.NotConnected,
                response.CloudRegistration!.State);
            Assert.False(response.CloudRegistration.HasCredential);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // G: enroll when already enrolled (no force) is rejected.
    [Fact]
    public async Task Enroll_AlreadyEnrolled_RejectedWithoutForce()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "existing", "existing-org", "old", null,
                DateTimeOffset.UtcNow);

            StubHttpMessageHandler stub = new(_ =>
                StubHttpMessageHandler.Json(
                    HttpStatusCode.Created,
                    "{\"deviceId\":\"new\",\"organizationId\":\"new-o\"," +
                    "\"credential\":\"c\"}"));
            CloudEnrollmentCoordinator coordinator = new(
                ClientWith(stub), store);
            CloudCommandHandler handler = new(coordinator, store);

            ServiceResponse response = await handler.EnrollAsync("X", null, force: false);

            Assert.False(response.Success);
            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal("existing", view.DeviceId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Reset clears local registration (conservative; does not contact Cloud).
    [Fact]
    public async Task Reset_ClearsLocalRegistration()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store =
                new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "device-1", "org-1", "cred", null,
                DateTimeOffset.UtcNow);

            CloudEnrollmentCoordinator coordinator = new(
                ClientWith(new StubHttpMessageHandler(_ =>
                    StubHttpMessageHandler.NoContent(HttpStatusCode.OK))),
                store);
            CloudCommandHandler handler = new(coordinator, store);

            ServiceResponse response = await handler.ResetAsync();
            Assert.True(response.Success);

            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.NotConnected, view.State);
            Assert.False(view.HasCredential);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempFile() =>
        Path.Combine(
            Path.GetTempPath(),
            "pvcloud-cmd-" + Guid.NewGuid().ToString("N") + ".json");
}
