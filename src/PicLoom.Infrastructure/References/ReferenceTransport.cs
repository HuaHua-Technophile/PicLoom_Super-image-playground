using System.Net;
using System.Net.Sockets;
using PicLoom.Core;
using PicLoom.Infrastructure.Providers;

namespace PicLoom.Infrastructure.References;

/// <summary>
/// Transport dedicated to user supplied reference URLs and upstream image downloads. On top of the
/// hardened defaults (no redirects, no decompression, short connect timeout) it validates the
/// address that is actually connected: the host is resolved inside <c>ConnectCallback</c> through
/// the injected <see cref="IDnsResolver"/>, every address is checked with
/// <see cref="ProviderUrl.IsRestrictedAddress"/> (all must be public), and the socket is opened to
/// one of those validated addresses. Resolving at connect time closes the DNS rebinding window, and
/// resolution failure rejects the request instead of falling through.
/// </summary>
public sealed class ReferenceTransport : IDisposable
{
    private readonly IDnsResolver _resolver;

    private readonly SocketsHttpHandler _handler;

    public ReferenceTransport(IDnsResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = ProviderTransport.ConnectTimeout,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // No proxy: the connect callback must see the real destination host so the address
            // check cannot be bypassed by a proxy CONNECT.
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) => ConnectAsync(context, _resolver, cancellationToken)
        };
    }

    /// <inheritdoc cref="IProviderTransport.Create"/>
    public HttpMessageHandler Create() => new SharedHandlerLease(_handler);

    /// <inheritdoc />
    public void Dispose() => _handler.Dispose();

    /// <summary>
    /// Resolves the destination and returns the validated endpoints. Exposed so tests can prove the
    /// socket target is the same address the validator approved.
    /// </summary>
    public static async Task<IReadOnlyList<IPEndPoint>> ResolveTargetsAsync(DnsEndPoint endpoint,
        IDnsResolver resolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var addresses = await ProviderUrl.ResolvePublicAddressesAsync(endpoint.Host, resolver,
            cancellationToken).ConfigureAwait(false);
        return addresses.Select(address => new IPEndPoint(address, endpoint.Port)).ToArray();
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context,
        IDnsResolver resolver, CancellationToken cancellationToken)
    {
        IReadOnlyList<IPEndPoint> targets;
        try
        {
            targets = await ResolveTargetsAsync(context.DnsEndPoint, resolver, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DomainException exception)
        {
            // Surface as a transport failure; callers map it to reference_url_invalid.
            throw new HttpRequestException(exception.Code);
        }

        Exception? last = null;
        foreach (var target in targets)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                socket.Dispose();
                last = exception;
            }
        }

        throw last ?? new HttpRequestException("reference_host_unreachable");
    }

    /// <summary>Delegating wrapper that never disposes the process-wide shared handler.</summary>
    private sealed class SharedHandlerLease : DelegatingHandler
    {
        public SharedHandlerLease(HttpMessageHandler inner) : base(inner)
        {
        }

        protected override void Dispose(bool disposing)
        {
            // The shared SocketsHttpHandler is owned by the singleton and disposed with the container.
        }
    }
}
