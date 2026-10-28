using System.Net;
using PicLoom.Core;

namespace PicLoom.Infrastructure.References;

/// <summary>
/// Downloads user supplied reference images. Only https URLs that survived
/// <see cref="ReferenceUrl.Parse"/> are fetched; redirects are disabled by the shared transport so
/// the request can never be bounced to a restricted host.
/// </summary>
public sealed class ReferenceFetcher
{
    /// <summary>Total budget for one download, including connection and body read.</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

    private readonly IHttpClientFactory _httpClientFactory;

    public ReferenceFetcher(IHttpClientFactory httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    /// <summary>Fetches the body of <paramref name="uri"/> with the documented size cap.</summary>
    public async Task<byte[]> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FetchTimeout);

        try
        {
            var client = _httpClientFactory.CreateClient(InfrastructureRegistration.ReferenceHttpClient);
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.TryAddWithoutValidation("Accept", "image/*");
            using var response = await client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new DomainException("reference_url_invalid");

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > ReferenceRules.MaxBytes)
                    throw new DomainException("reference_too_large");
                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0) throw new DomainException("reference_url_invalid");
            return buffer.ToArray();
        }
        catch (DomainException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Timeout, DNS failure, TLS failure or a truncated body: all unverifiable.
            throw new DomainException("reference_url_invalid");
        }
    }
}
