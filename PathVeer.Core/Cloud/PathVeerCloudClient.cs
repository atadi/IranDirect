using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using PathVeer.Core.Installation;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Cloud;

/// <summary>
/// Thin, explicit HTTP client for the certified PathVeer Cloud device
/// enrollment + heartbeat contract.
///
/// Transport guarantees:
/// - HTTPS only. Any non-https base URL is rejected at construction so a
///   misconfiguration can never send the device credential over cleartext.
/// - Bounded request timeout (caller-supplied; the registered instance uses a
///   conservative value).
/// - Bearer device credential on heartbeat only; never on enrollment.
/// - No automatic redirect that could forward the Authorization header to
///   another host: redirects are disabled, so a 3xx is treated as a failure
///   and the credential is never re-sent.
/// - No machine-identifying telemetry: heartbeat payload is only appVersion +
///   protocolVersion.
/// - Structured typed models; malformed responses fail closed.
/// - No secrets are logged.
/// </summary>
public sealed class PathVeerCloudClient
{
    public const string EnrollPath = "api/v1/devices/enroll";
    public const string HeartbeatPath = "api/v1/devices/heartbeat";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly IFaultInjectionPolicy _faultPolicy;

    /// <summary>
    /// Creates a client. In production <paramref name="httpClient"/> is the
    /// HttpClient provided by IHttpClientFactory (its primary handler must
    /// already disable automatic redirects so the Bearer credential is never
    /// forwarded to a redirected host). For tests, pass a client built from a
    /// stub handler and an https <paramref name="baseUrl"/>.
    /// </summary>
    public PathVeerCloudClient(
        string baseUrl,
        HttpClient httpClient,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "PathVeer Cloud base URL must be an absolute https:// URL.",
                nameof(baseUrl));
        }

        _baseUrl = baseUrl.TrimEnd('/');
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
        _httpClient = httpClient;
        _httpClient.BaseAddress = uri;
        _httpClient.Timeout = httpClient.Timeout;

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            ProductIdentity.UserAgent);
    }

    /// <summary>
    /// Test-only constructor: builds a client from a custom
    /// <paramref name="handler"/> (e.g. a stub) with no-auto-redirect
    /// enforced. Production code uses the HttpClient-accepting constructor.
    /// </summary>
    public PathVeerCloudClient(
        string baseUrl,
        HttpMessageHandler handler,
        TimeSpan timeout,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(handler);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "PathVeer Cloud base URL must be an absolute https:// URL.",
                nameof(baseUrl));
        }

        _baseUrl = baseUrl.TrimEnd('/');
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = uri,
            Timeout = timeout
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            ProductIdentity.UserAgent);
    }

    /// <summary>
    /// Exchanges a one-time enrollment code for a durable device identity +
    /// credential. The returned <see cref="EnrollResponse.Credential"/> is a
    /// secret and must be encrypted at rest by the caller immediately.
    /// </summary>
    public async Task<EnrollResponse> EnrollAsync(
        EnrollRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.EnrollmentCode))
        {
            throw new ArgumentException(
                "An enrollment code is required.",
                nameof(request));
        }

        if (FaultInjectionResolver.ShouldFail(
                _faultPolicy, FaultInjectionPoint.CloudHttp))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.CloudHttp);
        }

        object wire = new
        {
            enrollmentCode = request.EnrollmentCode,
            deviceLabel = request.DeviceLabel
        };

        using HttpRequestMessage httpRequest = new(
            HttpMethod.Post,
            EnrollPath)
        {
            Content = JsonContent.Create(
                wire,
                options: JsonOptions)
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // Includes HttpClient timeout. Fail closed as a transport error.
            throw new CloudEnrollmentException(
                "The request to PathVeer Cloud timed out.",
                EnrollmentFailureKind.Transport);
        }
        catch (HttpRequestException ex)
        {
            throw new CloudEnrollmentException(
                $"Could not reach PathVeer Cloud: {ex.Message}",
                EnrollmentFailureKind.Transport);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Single-use code already consumed or invalid.
            throw new CloudEnrollmentException(
                "The enrollment code was rejected (already used or invalid).",
                EnrollmentFailureKind.CodeRejected);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CloudEnrollmentException(
                $"Cloud enrollment failed with status " +
                $"{(int)response.StatusCode}.",
                EnrollmentFailureKind.Transport);
        }

        EnrollResponse? result;
        try
        {
            result =
                await response.Content.ReadFromJsonAsync<EnrollResponse>(
                    JsonOptions,
                    cancellationToken);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Malformed/non-JSON body: fail closed.
            throw new CloudEnrollmentException(
                $"Cloud returned an unparseable enrollment response: {ex.Message}",
                EnrollmentFailureKind.MalformedResponse);
        }

        if (result is null
            || string.IsNullOrWhiteSpace(result.DeviceId)
            || string.IsNullOrWhiteSpace(result.OrganizationId)
            || string.IsNullOrWhiteSpace(result.Credential))
        {
            // Malformed/partial response: fail closed. We must NOT treat this
            // as a successful enrollment and must NOT persist a half-identity.
            throw new CloudEnrollmentException(
                "Cloud returned an incomplete enrollment response.",
                EnrollmentFailureKind.MalformedResponse);
        }

        return result;
    }

    /// <summary>
    /// Sends a heartbeat. On a 401 with the certified revoked message, throws
    /// <see cref="CloudCredentialRevokedException"/> so the caller can stop
    /// using the credential. Any other failure throws
    /// <see cref="CloudHeartbeatException"/> (transient; safe to retry).
    /// </summary>
    public async Task<HeartbeatResponse> HeartbeatAsync(
        string credential,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        if (FaultInjectionResolver.ShouldFail(
                _faultPolicy, FaultInjectionPoint.CloudHttp))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.CloudHttp);
        }

        HeartbeatRequest wire = new()
        {
            AppVersion = appVersion,
            ProtocolVersion = "1"
        };

        using HttpRequestMessage httpRequest = new(
            HttpMethod.Post,
            HeartbeatPath)
        {
            Content = JsonContent.Create(
                wire,
                options: JsonOptions)
        };

        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", credential);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                cancellationToken);
        }
        catch (TaskCanceledException)
        {
            throw new CloudHeartbeatException(
                "The heartbeat request to PathVeer Cloud timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new CloudHeartbeatException(
                $"Could not reach PathVeer Cloud: {ex.Message}");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            string body = await SafeReadBodyAsync(
                response,
                cancellationToken);

            bool revoked = body.Contains(
                "revoked",
                StringComparison.OrdinalIgnoreCase);

            // ANY 401 means the credential is not accepted. We never retry with
            // the same credential (no storm), and we surface whether Cloud
            // reported an explicit revocation so the caller can mark local
            // state appropriately.
            throw new CloudHeartbeatAuthFailedException(
                revoked
                    ? "Device credential has been revoked."
                    : "Heartbeat rejected with 401 (credential not accepted).",
                revoked);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CloudHeartbeatException(
                $"Heartbeat failed with status " +
                $"{(int)response.StatusCode}.");
        }

        HeartbeatResponse? result;
        try
        {
            result =
                await response.Content.ReadFromJsonAsync<HeartbeatResponse>(
                    JsonOptions,
                    cancellationToken);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new CloudHeartbeatException(
                $"Cloud returned an unparseable heartbeat response: {ex.Message}");
        }

        if (result is null
            || string.IsNullOrWhiteSpace(result.DeviceId)
            || string.IsNullOrWhiteSpace(result.OrganizationId))
        {
            throw new CloudHeartbeatException(
                "Cloud returned an incomplete heartbeat response.");
        }

        return result;
    }

    private static async Task<string> SafeReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(
                cancellationToken);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

/// <summary>Why an enrollment attempt failed.</summary>
public enum EnrollmentFailureKind
{
    Transport,
    CodeRejected,
    MalformedResponse
}

/// <summary>Thrown when Cloud rejects an enrollment attempt.</summary>
public sealed class CloudEnrollmentException : Exception
{
    public EnrollmentFailureKind Kind { get; }

    public CloudEnrollmentException(
        string message,
        EnrollmentFailureKind kind)
        : base(message)
    {
        Kind = kind;
    }
}

/// <summary>
/// Thrown when a heartbeat receives a 401 for the device credential. The
/// caller must stop using the credential. <see cref="Revoked"/> distinguishes
/// an explicit Cloud revocation from other auth failures.
/// </summary>
public sealed class CloudHeartbeatAuthFailedException : Exception
{
    public bool Revoked { get; }

    public CloudHeartbeatAuthFailedException(
        string message,
        bool revoked)
        : base(message)
    {
        Revoked = revoked;
    }
}

/// <summary>Thrown on any heartbeat failure (transient unless revoked).</summary>
public sealed class CloudHeartbeatException : Exception
{
    public CloudHeartbeatException(string message)
        : base(message)
    {
    }
}
