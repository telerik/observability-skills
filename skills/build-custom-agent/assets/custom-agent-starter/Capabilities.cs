using System.Net;

namespace CustomAgent;

/// <summary>
/// What the tools may reach beyond the bundled content: the Capabilities section of appsettings.json, approved by
/// the user before the build. This version grants one capability, read-only GET requests to the hosts listed in
/// Capabilities:Network:AllowedHosts, through the ApprovedHttpClient that Program.cs passes to
/// AgentDefinition.CreateTools. GetAsync returns every status with its body, so a tool can report data the API does
/// not have; GetStringAsync returns only a successful body. ApprovedHttpResponse.EnsureSuccessStatusCode fails the
/// tool call on any other status, like HttpResponseMessage.EnsureSuccessStatusCode. Process execution, file writes
/// and environment or secret reads are not granted.
/// </summary>
public sealed record Capabilities(NetworkCapability Network)
{
    public const int MaxAllowedHosts = 5;

    public static Capabilities Load(IConfiguration configuration)
    {
        var hosts = configuration.GetSection("Capabilities:Network:AllowedHosts")
            .GetChildren()
            .Select(item => item.Value?.Trim() ?? "")
            .ToArray();
        if (hosts.Length > MaxAllowedHosts)
            throw new InvalidOperationException($"Capabilities:Network:AllowedHosts may list at most {MaxAllowedHosts} hosts.");
        // A host is a DNS name or IPv4 address; "https://host", "host:443" and "host/path" are rejected.
        var invalid = hosts.FirstOrDefault(host => Uri.CheckHostName(host) is not (UriHostNameType.Dns or UriHostNameType.IPv4));
        if (invalid is not null)
            throw new InvalidOperationException($"Capabilities:Network:AllowedHosts entries must be host names without scheme, port or path: '{invalid}'.");
        if (hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hosts.Length)
            throw new InvalidOperationException("Capabilities:Network:AllowedHosts must not repeat a host.");
        return new Capabilities(new NetworkCapability(hosts));
    }
}

public sealed record NetworkCapability(IReadOnlyList<string> AllowedHosts);

public sealed class ApprovedHttpClient : IDisposable
{
    public const int MaxResponseBytes = 65_536;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _client;

    public ApprovedHttpClient(IReadOnlyList<string> allowedHosts, HttpMessageHandler? handler = null)
    {
        AllowedHosts = allowedHosts;
        // Redirects are not followed: a response could otherwise send the request on to a host that was not approved.
        _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = MaxResponseBytes,
        };
    }

    public IReadOnlyList<string> AllowedHosts { get; }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
        => (await GetAsync(url, cancellationToken)).EnsureSuccessStatusCode().Body;

    public async Task<ApprovedHttpResponse> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        // The refusal names only the host: a rejected URL may carry credentials.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new NetworkAccessException("network_url_invalid", uri?.Host ?? "unparsed");
        // Plain HTTP is accepted only for loopback addresses, such as a mock API running on this machine.
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new NetworkAccessException("network_https_required", uri.Host);
        if (!AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new NetworkAccessException("network_host_not_approved", uri.Host);

        // Any status returns with its body, read as text and capped at MaxResponseBytes. An oversized body, a
        // timeout or a failed connection fails the tool call, and the tool loop then fails the run.
        try
        {
            using var response = await _client.GetAsync(uri, cancellationToken);
            return new ApprovedHttpResponse(
                uri.Host,
                (int)response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (HttpRequestException error)
        {
            throw new NetworkAccessException(
                error.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded
                    ? "network_response_too_large"
                    : "network_request_failed",
                uri.Host);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NetworkAccessException("network_timeout", uri.Host);
        }
    }

    public void Dispose() => _client.Dispose();
}

public sealed record ApprovedHttpResponse(string Host, int StatusCode, string Body)
{
    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;

    public ApprovedHttpResponse EnsureSuccessStatusCode()
        => IsSuccessStatusCode ? this : throw new NetworkAccessException($"network_http_{StatusCode}", Host);
}

public sealed class NetworkAccessException(string reason, string detail) : Exception($"{reason}: {detail}")
{
    public string Reason { get; } = reason;
}
