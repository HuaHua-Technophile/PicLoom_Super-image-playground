using System.Net;

namespace PicLoom.Infrastructure.Providers;

/// <summary>Supplies the message handler used by the OpenAI compatible HTTP client.</summary>
public interface IProviderTransport
{
    /// <summary>Creates the handler to use. The returned handler is owned by the caller.</summary>
    HttpMessageHandler Create();
}

/// <summary>
/// Default transport: no automatic redirects (a 3xx must never leak the API key to another host),
/// a 5 second connect timeout and no transparent decompression. The same underlying handler is
/// reused for the lifetime of the process, which makes it replaceable in tests.
/// </summary>
public sealed class ProviderTransport : IProviderTransport, IDisposable
{
    /// <summary>Connect timeout for every upstream connection.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly SocketsHttpHandler _handler = new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = ConnectTimeout,
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    /// <inheritdoc />
    public HttpMessageHandler Create() => new SharedHandlerLease(_handler);

    /// <inheritdoc />
    public void Dispose() => _handler.Dispose();

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
