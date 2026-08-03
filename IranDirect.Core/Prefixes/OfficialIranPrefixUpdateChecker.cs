using System.Net;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Prefixes;

public sealed class OfficialIranPrefixUpdateChecker :
    IPrefixUpdateChecker
{
    private const int MaxReasonLength = 300;

    private readonly HttpClient _httpClient;
    private readonly IPrefixSourceMetadataService _metadataService;
    private readonly PrefixUpdateCheckOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IFaultInjectionPolicy _faultPolicy;

    public OfficialIranPrefixUpdateChecker(
        HttpClient httpClient,
        IPrefixSourceMetadataService metadataService,
        PrefixUpdateCheckOptions options,
        TimeProvider? timeProvider = null,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        _httpClient = httpClient;
        _metadataService = metadataService;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public async Task<PrefixUpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        DateTimeOffset checkedAt = _timeProvider.GetUtcNow();

        PrefixSourceMetadata? current;
        try
        {
            current = await _metadataService.GetCurrentAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Unknown,
                CheckedAt = checkedAt,
                Reason = Truncate(
                    $"Local metadata unavailable: {ex.Message}")
            };
        }

        if (current is null)
        {
            return new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Unknown,
                CheckedAt = checkedAt,
                Reason = "No local metadata available."
            };
        }

        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            PrefixUpdateCheckRemoteMetadata remote =
                await ProbeRemoteAsync(timeout.Token);

            return Compare(current, remote, checkedAt);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Failed,
                CurrentMetadata = current,
                CheckedAt = checkedAt,
                Reason = $"Remote check timed out after " +
                    $"{_options.Timeout.TotalSeconds:0.#} seconds."
            };
        }
        catch (Exception ex)
        {
            return new PrefixUpdateCheckResult
            {
                Status = PrefixUpdateCheckStatus.Failed,
                CurrentMetadata = current,
                CheckedAt = checkedAt,
                Reason = Truncate(
                    $"Remote check failed: {ex.Message}")
            };
        }
    }

    private async Task<PrefixUpdateCheckRemoteMetadata>
        ProbeRemoteAsync(CancellationToken cancellationToken)
    {
        if (FaultInjectionResolver.ShouldFail(
                _faultPolicy,
                FaultInjectionPoint.HttpRequest))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.HttpRequest);
        }

        using HttpRequestMessage headRequest = new(
            HttpMethod.Head,
            OfficialIranPrefixSource.Descriptor.Uri);

        using HttpResponseMessage headResponse =
            await _httpClient.SendAsync(
                headRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (headResponse.StatusCode == HttpStatusCode.MethodNotAllowed
            || headResponse.StatusCode == HttpStatusCode.NotImplemented)
        {
            return await FetchRemoteMetadataAsync(
                cancellationToken);
        }

        headResponse.EnsureSuccessStatusCode();

        return ReadMetadata(headResponse);
    }

    private async Task<PrefixUpdateCheckRemoteMetadata>
        FetchRemoteMetadataAsync(
            CancellationToken cancellationToken)
    {
        if (FaultInjectionResolver.ShouldFail(
                _faultPolicy,
                FaultInjectionPoint.HttpRequest))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.HttpRequest);
        }

        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                OfficialIranPrefixSource.Descriptor.Uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        return ReadMetadata(response);
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
            return UpdateAvailable(
                current,
                remote,
                checkedAt);
        }

        if (hasLastModified
            && current.SourceLastModified is { } localModified
            && remote.LastModified > localModified)
        {
            return UpdateAvailable(
                current,
                remote,
                checkedAt);
        }

        if (hasContentLength
            && current.ContentLength is { } localLength
            && remote.ContentLength != localLength)
        {
            return UpdateAvailable(
                current,
                remote,
                checkedAt);
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
