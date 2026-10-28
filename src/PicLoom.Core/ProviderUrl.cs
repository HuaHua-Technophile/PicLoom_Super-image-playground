using System.Net;
using System.Net.Sockets;

namespace PicLoom.Core;

/// <summary>A normalized OpenAI-compatible base URL together with its derived endpoints.</summary>
/// <param name="BaseUrl">Absolute URL without a trailing slash and without a trailing <c>/v1</c>.</param>
/// <param name="Origin">Lower-case <c>scheme://host[:port]</c> used as the secret-store key.</param>
/// <param name="ImagesUrl"><c>{BaseUrl}/v1/images/generations</c>.</param>
/// <param name="ModelsUrl"><c>{BaseUrl}/v1/models</c>.</param>
/// <param name="EditsUrl"><c>{BaseUrl}/v1/images/edits</c> (image-to-image, multipart).</param>
public sealed record NormalizedBaseUrl(string BaseUrl, string Origin, string ImagesUrl, string ModelsUrl,
    string EditsUrl);

/// <summary>Resolves DNS names into addresses. Injectable so tests can script the answers.</summary>
public interface IDnsResolver
{
    /// <summary>Resolves every A/AAAA record for <paramref name="host"/>.</summary>
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

/// <summary>Default resolver backed by the operating system.</summary>
public sealed class SystemDnsResolver : IDnsResolver
{
    /// <inheritdoc />
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>
/// Validates and normalizes user supplied Base URLs. Only https is accepted; plain http is
/// tolerated exclusively for loopback hosts (local relays and test stubs), because loopback
/// traffic never leaves the machine.
/// </summary>
public static class ProviderUrl
{
    /// <summary>Relative path of the image generation endpoint.</summary>
    public const string GenerationsPath = "/v1/images/generations";

    /// <summary>Relative path of the image edit endpoint (image-to-image).</summary>
    public const string EditsPath = "/v1/images/edits";

    /// <summary>Relative path of the model listing endpoint used by "test connection".</summary>
    public const string ModelsPath = "/v1/models";

    private const string VersionSuffix = "/v1";

    /// <summary>
    /// Normalizes a Base URL or throws a <see cref="DomainException"/> with
    /// <c>base_url_required</c> (<c>invalid_base_url</c> when malformed).
    /// </summary>
    /// <remarks>
    /// There is deliberately <b>no</b> default and no fallback host: an empty or whitespace Base URL
    /// is rejected instead of being replaced by a first-party address. The stored API key belongs to
    /// the user's own relay, so substituting a host here would ship that credential to a third party.
    /// Callers that need "no configuration yet" semantics translate the error themselves
    /// (the task path reports <c>provider_not_configured</c>).
    /// </remarks>
    public static NormalizedBaseUrl Normalize(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new DomainException("base_url_required");

        var raw = baseUrl.Trim();
        // Reject traversal segments before Uri collapses them away.
        if (raw.Contains("..", StringComparison.Ordinal)) throw new DomainException("invalid_base_url");

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new DomainException("invalid_base_url");

        var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !(isHttp && IsLoopback(uri))) throw new DomainException("invalid_base_url");

        var authority = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        var origin = authority.ToLowerInvariant();

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(VersionSuffix, StringComparison.Ordinal))
            path = path[..^VersionSuffix.Length].TrimEnd('/');

        var normalized = authority + path;
        return new NormalizedBaseUrl(normalized, origin, normalized + GenerationsPath, normalized + ModelsPath,
            normalized + EditsPath);
    }

    /// <summary>True when the host resolves to the local machine (localhost, 127.0.0.0/8, ::1).</summary>
    public static bool IsLoopback(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.IsLoopback) return true;

        var host = uri.DnsSafeHost;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "::1", StringComparison.Ordinal);
    }

    /// <summary>
    /// True for hosts that must never be fetched as a remote image URL (loopback, link-local,
    /// private ranges, multicast). Used to keep the <c>url</c> response form and reference URL
    /// imports from becoming an SSRF probe.
    /// </summary>
    /// <remarks>
    /// Host names are normalised first (lower-case, trailing dot removed), then IPv4-mapped IPv6
    /// literals are translated back to IPv4 so <c>::ffff:127.0.0.1</c> cannot slip past the IPv4
    /// branches. Anything that is not an IP literal is treated as a DNS name; only <c>localhost</c>
    /// (and its <c>*.localhost</c> form) is refused here by name. DNS names are resolved on the
    /// connection path (the reference transport's <c>ConnectCallback</c>) where every resolved
    /// address is checked with <see cref="IsRestrictedAddress"/>, which also closes the DNS
    /// rebinding window between validation and connect.
    /// </remarks>
    public static bool IsRestrictedHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (IsLoopback(uri)) return true;

        var host = uri.DnsSafeHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return true;
        if (string.Equals(host, "localhost", StringComparison.Ordinal)
            || host.EndsWith(".localhost", StringComparison.Ordinal))
            return true;

        return IPAddress.TryParse(host, out var address) && IsRestrictedAddress(address);
    }

    /// <summary>
    /// Resolves <paramref name="host"/> and rejects it when any resolved address (or a literal) is
    /// loopback/private/link-local/ULA/unspecified. Failure to resolve, an empty answer and a
    /// restricted answer all surface as <c>reference_url_invalid</c>.
    /// </summary>
    /// <remarks>
    /// This is the authoritative check: the reference transport resolves here inside its
    /// <c>ConnectCallback</c> and then connects to one of the returned addresses, so validation and
    /// the actual destination are the same address (no rebinding window).
    /// </remarks>
    public static async Task<IReadOnlyList<IPAddress>> ResolvePublicAddressesAsync(string host,
        IDnsResolver resolver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var normalized = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.Length == 0) throw new DomainException("reference_url_invalid");
        if (string.Equals(normalized, "localhost", StringComparison.Ordinal)
            || normalized.EndsWith(".localhost", StringComparison.Ordinal))
            throw new DomainException("reference_url_invalid");

        if (IPAddress.TryParse(normalized.Trim('[', ']'), out var literal))
        {
            if (IsRestrictedAddress(literal)) throw new DomainException("reference_url_invalid");
            return [literal];
        }

        IPAddress[] addresses;
        try
        {
            addresses = await resolver.ResolveAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DomainException("reference_url_invalid");
        }

        if (addresses is not { Length: > 0 }) throw new DomainException("reference_url_invalid");
        foreach (var address in addresses)
        {
            if (IsRestrictedAddress(address)) throw new DomainException("reference_url_invalid");
        }

        return addresses;
    }

    /// <summary>URI overload of <see cref="ResolvePublicAddressesAsync(string, IDnsResolver, CancellationToken)"/>.</summary>
    public static Task<IReadOnlyList<IPAddress>> ResolvePublicAddressesAsync(Uri uri, IDnsResolver resolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return ResolvePublicAddressesAsync(uri.DnsSafeHost, resolver, cancellationToken);
    }

    /// <summary>
    /// True when an already resolved address is loopback, private, link-local, multicast,
    /// unspecified or IPv4-mapped to one of those. Shared by the literal check above and the
    /// connect-time DNS check so both use the exact same policy.
    /// </summary>
    public static bool IsRestrictedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6) return IsRestrictedAddress(address.MapToIPv4());

            if (address.Equals(IPAddress.IPv6Loopback) || address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.IPv6None) || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast)
                return true;
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork) return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
               || bytes[0] == 10
               || bytes[0] == 127
               || bytes[0] >= 224 // multicast + reserved
               || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
               || (bytes[0] == 192 && bytes[1] == 168)
               || (bytes[0] == 169 && bytes[1] == 254);
    }
}
