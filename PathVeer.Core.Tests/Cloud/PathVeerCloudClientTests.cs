using System.Net;
using System.Net.Http;
using PathVeer.Core.Cloud;
using PathVeer.TestSupport.Http;
using Xunit;

namespace PathVeer.Core.Tests.Cloud;

public sealed class PathVeerCloudClientTests
{
    private const string BaseUrl = "https://cloud.pathveer.test";

    private static PathVeerCloudClient CreateClient(
        StubHttpMessageHandler stub,
        TimeSpan? timeout = null) =>
        new(
            BaseUrl,
            stub,
            timeout ?? TimeSpan.FromSeconds(10));

    // A. enrollment request contract is correct.
    [Fact]
    public async Task EnrollAsync_SendsCorrectRequestContract()
    {
        StubHttpMessageHandler stub = new(_ =>
            StubHttpMessageHandler.Json(
                HttpStatusCode.Created,
                "{\"deviceId\":\"d1\",\"organizationId\":\"o1\"," +
                "\"credential\":\"sec\"}"));

        PathVeerCloudClient client = CreateClient(stub);

        EnrollResponse response = await client.EnrollAsync(
            new EnrollRequest
            {
                EnrollmentCode = "CODE123",
                DeviceLabel = "My Laptop"
            });

        Assert.Equal("d1", response.DeviceId);
        Assert.Equal("o1", response.OrganizationId);
        Assert.Equal("sec", response.Credential);

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.EndsWith(
            "api/v1/devices/enroll",
            stub.LastRequest.RequestUri!.ToString());
        // No Authorization header on enrollment.
        Assert.Null(stub.LastRequest.Headers.Authorization);
        Assert.Contains("\"enrollmentCode\":\"CODE123\"", stub.LastRequestBody);
        Assert.Contains("\"deviceLabel\":\"My Laptop\"", stub.LastRequestBody);
    }

    [Fact]
    public async Task EnrollAsync_RejectsNonHttpsBaseUrl()
    {
        Assert.Throws<ArgumentException>(() =>
            new PathVeerCloudClient(
                "http://insecure.example",
                new StubHttpMessageHandler(_ =>
                    StubHttpMessageHandler.NoContent(HttpStatusCode.OK)),
                TimeSpan.FromSeconds(5)));
    }

    // E. malformed enrollment response fails closed.
    [Theory]
    [InlineData("{}")]                                  // missing fields
    [InlineData("{\"deviceId\":\"d1\"}")]                // missing org + cred
    [InlineData("not json at all")]                     // unparseable
    public async Task EnrollAsync_MalformedResponse_FailsClosed(
        string body)
    {
        StubHttpMessageHandler stub = new(_ =>
            StubHttpMessageHandler.Json(HttpStatusCode.Created, body));
        PathVeerCloudClient client = CreateClient(stub);

        CloudEnrollmentException ex =
            await Assert.ThrowsAsync<CloudEnrollmentException>(
                () => client.EnrollAsync(
                    new EnrollRequest { EnrollmentCode = "X" }));

        Assert.Equal(
            EnrollmentFailureKind.MalformedResponse,
            ex.Kind);
    }

    [Fact]
    public async Task EnrollAsync_RejectedCode_FailsClosedAsCodeRejected()
    {
        StubHttpMessageHandler stub = new(_ =>
            StubHttpMessageHandler.NoContent(HttpStatusCode.Unauthorized));
        PathVeerCloudClient client = CreateClient(stub);

        CloudEnrollmentException ex =
            await Assert.ThrowsAsync<CloudEnrollmentException>(
                () => client.EnrollAsync(
                    new EnrollRequest { EnrollmentCode = "USED" }));

        Assert.Equal(
            EnrollmentFailureKind.CodeRejected,
            ex.Kind);
    }

    // H. heartbeat includes ONLY appVersion + protocolVersion.
    [Fact]
    public async Task HeartbeatAsync_SendsOnlyAllowedTelemetry()
    {
        StubHttpMessageHandler stub = new(_ =>
            StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                "{\"deviceId\":\"d1\",\"organizationId\":\"o1\"," +
                "\"status\":\"active\",\"serverTime\":\"2026-01-01T00:00:00Z\"}"));
        PathVeerCloudClient client = CreateClient(stub);

        await client.HeartbeatAsync("cred", "1.0.0");

        Assert.EndsWith(
            "api/v1/devices/heartbeat",
            stub.LastRequest!.RequestUri!.ToString());
        // Exactly the two allowed fields, nothing machine-identifying.
        Assert.Contains("\"appVersion\":\"1.0.0\"", stub.LastRequestBody);
        Assert.Contains("\"protocolVersion\":\"1\"", stub.LastRequestBody);
        Assert.DoesNotContain("hostname", stub.LastRequestBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip", stub.LastRequestBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machine", stub.LastRequestBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("route", stub.LastRequestBody,
            StringComparison.OrdinalIgnoreCase);
    }

    // I. heartbeat sends Bearer credential.
    [Fact]
    public async Task HeartbeatAsync_SendsBearerCredential()
    {
        StubHttpMessageHandler stub = new(_ =>
            StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                "{\"deviceId\":\"d1\",\"organizationId\":\"o1\"," +
                "\"status\":\"active\",\"serverTime\":\"2026-01-01T00:00:00Z\"}"));
        PathVeerCloudClient client = CreateClient(stub);

        await client.HeartbeatAsync("RAWCRED", "1.0.0");

        Assert.Equal(
            "Bearer RAWCRED",
            stub.LastAuthorizationHeader);
    }

    // No automatic redirect that could forward Authorization to another host.
    [Fact]
    public async Task HeartbeatAsync_OnRedirect_DoesNotFollowAndFails()
    {
        StubHttpMessageHandler stub = new(_ =>
            StubHttpMessageHandler.NoContent(
                HttpStatusCode.Redirect));
        // The client handler has AllowAutoRedirect=false (production) — but
        // the test stub returns the 3xx directly. The client must treat any
        // non-2xx (including 3xx) as a failure and NOT retry with auth.
        PathVeerCloudClient client = CreateClient(stub);

        await Assert.ThrowsAsync<CloudHeartbeatException>(
            () => client.HeartbeatAsync("RAWCRED", "1.0.0"));
    }

    // Revoked credential -> explicit revocation exception.
    [Fact]
    public async Task HeartbeatAsync_RevokedCredential_ThrowsRevoked()
    {
        StubHttpMessageHandler stub = new(_ =>
            StubHttpMessageHandler.Json(
                HttpStatusCode.Unauthorized,
                "{\"error\":\"Device credential has been revoked.\"}"));
        PathVeerCloudClient client = CreateClient(stub);

        CloudHeartbeatAuthFailedException ex =
            await Assert.ThrowsAsync<CloudHeartbeatAuthFailedException>(
                () => client.HeartbeatAsync("RAWCRED", "1.0.0"));

        Assert.True(ex.Revoked);
    }
}
