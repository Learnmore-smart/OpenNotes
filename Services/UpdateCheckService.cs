#nullable enable

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Caelum.Services;

public enum UpdateCheckFailureKind
{
    Network,
    Timeout,
    HttpStatus,
    InvalidResponse
}

public sealed record UpdateCheckResult(
    Version InstalledVersion,
    Version LatestVersion,
    Uri ReleaseUri)
{
    public bool IsUpdateAvailable => LatestVersion > InstalledVersion;
}

public sealed class UpdateCheckException : Exception
{
    public UpdateCheckException(
        UpdateCheckFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public UpdateCheckFailureKind Kind { get; }
}

public sealed class UpdateCheckService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const string ReleasePathPrefix =
        "/Learnmore-smart/Windows-Notes/releases/";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    public static readonly Uri LatestReleaseApiUri =
        new("https://api.github.com/repos/Learnmore-smart/Windows-Notes/releases/latest");

    public UpdateCheckService(HttpClient? httpClient = null)
        : this(httpClient ?? new HttpClient(), DefaultTimeout)
    {
    }

    internal UpdateCheckService(HttpClient httpClient, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version installedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        Version normalizedInstalled = NormalizeVersion(installedVersion);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUri);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd($"OpenNotes/{ProductInfo.Version}");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        try
        {
            using HttpResponseMessage response =
                await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateCheckException(
                    UpdateCheckFailureKind.HttpStatus,
                    $"GitHub returned HTTP {(int)response.StatusCode}.");
            }

            string json = await response.Content
                .ReadAsStringAsync(timeout.Token)
                .ConfigureAwait(false);
            return ParseResponse(json, normalizedInstalled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new UpdateCheckException(
                UpdateCheckFailureKind.Timeout,
                "The update request timed out.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateCheckException(
                UpdateCheckFailureKind.Network,
                "The update request could not reach GitHub.",
                ex);
        }
    }

    internal static Version ParseReleaseVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new FormatException("The release tag is empty.");

        string normalized = tag.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        string[] parts = normalized.Split('.');
        if (parts.Length is < 2 or > 4)
            throw new FormatException("The release tag must have two to four parts.");

        var values = new int[4];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out int value) || value < 0)
                throw new FormatException("The release tag contains a non-numeric part.");

            values[index] = value;
        }

        return new Version(values[0], values[1], values[2], values[3]);
    }

    internal static bool IsTrustedReleaseUri(Uri? uri)
    {
        return uri is { IsAbsoluteUri: true } &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith(ReleasePathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static UpdateCheckResult ParseResponse(
        string json,
        Version installedVersion)
    {
        try
        {
            var release = JsonSerializer.Deserialize<LatestReleaseResponse>(json);
            Version latestVersion = ParseReleaseVersion(release?.TagName ?? string.Empty);
            if (!Uri.TryCreate(release?.HtmlUrl, UriKind.Absolute, out Uri? releaseUri) ||
                !IsTrustedReleaseUri(releaseUri))
            {
                throw new FormatException("The release URL is missing or untrusted.");
            }

            return new UpdateCheckResult(installedVersion, latestVersion, releaseUri);
        }
        catch (Exception ex) when (
            ex is JsonException or FormatException or OverflowException)
        {
            throw new UpdateCheckException(
                UpdateCheckFailureKind.InvalidResponse,
                "GitHub returned an invalid release response.",
                ex);
        }
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    private sealed class LatestReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
