using System.Net;
using PathVeer.Core.Configuration;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Prefixes;

/// <summary>
/// Generic country-prefix update checker. Replaces the Iran-specific checker.
/// Probes the per-country RIPEstat resource URL and compares ETag /
/// Last-Modified / Content-Length against the country-scoped local metadata,
/// so an IR update can never suppress an IQ update (and vice versa).
///
/// Implements both <see cref="ICountryPrefixUpdateChecker"/> (explicit country)
/// and <see cref="IPrefixUpdateChecker"/> (monitor path, where the country is
/// resolved via the configured provider).
/// </summary>
public sealed class CountryPrefixUpdateChecker :
    ICountryPrefixUpdateChecker,
    IPrefixUpdateChecker
{
    private const int MaxReasonLength = 300;

    private readonly ICountryPrefixSource _source;
    private readonly IPrefixSourceMetadataService _metadataService;
    private readonly PrefixUpdateCheckOptions _options;
    private readonly Func<DirectCountryCode>? _countryProvider;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly IFaultInjectionPolicy _faultPolicy;

    public CountryPrefixUpdateChecker(
        ICountryPrefixSource source,
        IPrefixSourceMetadataService metadataService,
        PrefixUpdateCheckOptions options,
        HttpClient httpClient,
        Func<DirectCountryCode>? countryProvider = null,
        TimeProvider? timeProvider = null,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        _source = source;
        _metadataService = metadataService;
        _options = options;
        _countryProvider = countryProvider;
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public Task<PrefixUpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken) =>
        CheckAsync(
            ResolveCountry(),
            cancellationToken);

    public async Task<PrefixUpdateCheckResult> CheckAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(country);

        DateTimeOffset checkedAt = _timeProvider.GetUtcNow();

        PrefixUpdateCheckResult result;
        using var scope = PrefixUpdateTelemetry.StartCheck();

        PrefixSourceMetadata? current;
        try
        {
            current = await _metadataService.GetCurrentAsync(
                country, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            scope.CompleteCancelled();
            throw;
        }
        catch (Exception ex)
        {
            result = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Unknown,
                CheckedAt = checkedAt,
                Reason = Truncate(
                    $"Local metadata unavailable: {ex.Message}")
            };
            scope.Complete(result.Status);
            return result;
        }

        if (current is null)
        {
            result = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Unknown,
                CheckedAt = checkedAt,
                Reason = "No local metadata available."
            };
            scope.Complete(result.Status);
            return result;
        }

        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            PrefixUpdateCheckRemoteMetadata remote =
                await ProbeRemoteAsync(
                    country, scope, timeout.Token);

            bool canCompare = remote.ETag is not null
                || remote.LastModified is not null
                || remote.ContentLength is not null;
            using var compareScope = canCompare
                ? scope.StartCompare()
                : null;
            result = Compare(current, remote, checkedAt);
            compareScope?.CompleteSuccess();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            scope.CompleteCancelled();
            throw;
        }
        catch (OperationCanceledException)
        {
            result = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Failed,
                CurrentMetadata = current,
                CheckedAt = checkedAt,
                Reason = $"Remote check timed out after " +
                    $"{_options.Timeout.TotalSeconds:0.#} seconds."
            };
            scope.Complete(result.Status);
            return result;
        }
        catch (Exception ex)
        {
            result = new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Failed,
                CurrentMetadata = current,
                CheckedAt = checkedAt,
                Reason = Truncate(
                    $"Remote check failed: {ex.Message}")
            };
            scope.CompleteFailure(ex);
            return result;
        }

        scope.Complete(result.Status);
        return result;
    }

    private DirectCountryCode ResolveCountry()
    {
        if (_countryProvider is null)
        {
            throw new InvalidOperationException(
                "CountryPrefixUpdateChecker was created without a country " +
                "provider; use CheckAsync(DirectCountryCode, ...) for an " +
                "explicit country.");
        }

        return _countryProvider();
    }

    private async Task<PrefixUpdateCheckRemoteMetadata>
        ProbeRemoteAsync(
            DirectCountryCode country,
            PrefixUpdateTelemetry.PrefixCheckScope scope,
            CancellationToken cancellationToken)
    {
        if (FaultInjectionResolver.ShouldFail(
                _faultPolicy,
                FaultInjectionPoint.HttpRequest))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.HttpRequest);
        }

        using HttpResponseMessage headResponse =
            await SendHeadAsync(
                country, scope, cancellationToken);

        if (headResponse.StatusCode == HttpStatusCode.MethodNotAllowed
            || headResponse.StatusCode == HttpStatusCode.NotImplemented)
        {
            return await FetchRemoteMetadataAsync(
                country, scope, cancellationToken);
        }

        headResponse.EnsureSuccessStatusCode();

        return ReadMetadata(headResponse);
    }

    private async Task<HttpResponseMessage> SendHeadAsync(
        DirectCountryCode country,
        PrefixUpdateTelemetry.PrefixCheckScope scope,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage headRequest = new(
            HttpMethod.Head,
            _source.GetDescriptor(country).Uri);

        using var headScope = scope.StartHead();
        try
        {
            HttpResponseMessage headResponse = await _httpClient.SendAsync(
                headRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            headScope.CompleteSuccess();
            return headResponse;
        }
        catch (Exception ex)
        {
            headScope.CompleteFailure(ex);
            throw;
        }
    }

    private async Task<PrefixUpdateCheckRemoteMetadata>
        FetchRemoteMetadataAsync(
            DirectCountryCode country,
            PrefixUpdateTelemetry.PrefixCheckScope scope,
            CancellationToken cancellationToken)
    {
        if (FaultInjectionResolver.ShouldFail(
                _faultPolicy,
                FaultInjectionPoint.HttpRequest))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.HttpRequest);
        }

        using var getScope = scope.StartGet();
        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(
                    _source.GetDescriptor(country).Uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            response.EnsureSuccessStatusCode();
            getScope.CompleteSuccess();
            return ReadMetadata(response);
        }
        catch (Exception ex)
        {
            getScope.CompleteFailure(ex);
            throw;
        }
    }

    private static PrefixUpdateCheckRemoteMetadata ReadMetadata(
        HttpResponseMessage response) =>
        new()
        {
            ETag = response.Headers.ETag?.Tag,
            LastModified = response.Content.Headers.LastModified,
            ContentLength = response.Content.Headers.ContentLength
        };

    private static PrefixUpdateCheckResult Compare(
        PrefixSourceMetadata current,
        PrefixUpdateCheckRemoteMetadata remote,
        DateTimeOffset checkedAt)
    {
        bool hasEtag = remote.ETag is not null;
        bool hasLastModified = remote.LastModified is not null;
        bool hasContentLength = remote.ContentLength is not null;

        if (!hasEtag && !hasLastModified && !hasContentLength)
        {
            return new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Unknown,
                CurrentMetadata = current,
                RemoteMetadata = remote,
                CheckedAt = checkedAt,
                Reason = "Remote did not expose comparison metadata."
            };
        }

        if (hasEtag
            && !string.Equals(
                current.ETag,
                remote.ETag,
                StringComparison.Ordinal))
        {
            return UpdateAvailable(current, remote, checkedAt);
        }

        if (hasLastModified
            && current.SourceLastModified is { } localModified
            && remote.LastModified > localModified)
        {
            return UpdateAvailable(current, remote, checkedAt);
        }

        if (hasContentLength
            && current.ContentLength is { } localLength
            && remote.ContentLength != localLength)
        {
            return UpdateAvailable(current, remote, checkedAt);
        }

        return new PrefixUpdateCheckResult
        {
            Status = PrefixUpdateCheckStatus.Current,
            CurrentMetadata = current,
            RemoteMetadata = remote,
            CheckedAt = checkedAt
        };
    }

    private static PrefixUpdateCheckResult UpdateAvailable(
        PrefixSourceMetadata current,
        PrefixUpdateCheckRemoteMetadata remote,
        DateTimeOffset checkedAt) =>
        new()
        {
            Status = PrefixUpdateCheckStatus.UpdateAvailable,
            CurrentMetadata = current,
            RemoteMetadata = remote,
            CheckedAt = checkedAt
        };

    private static string Truncate(string value) =>
        value.Length <= MaxReasonLength
            ? value
            : value[..MaxReasonLength];
}
