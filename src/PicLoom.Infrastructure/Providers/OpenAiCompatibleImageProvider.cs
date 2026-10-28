using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using PicLoom.Core;

namespace PicLoom.Infrastructure.Providers;

/// <summary>
/// OpenAI compatible image adapter used with BYOK relays. Exactly one POST is issued per attempt;
/// no retry, no model substitution, no redirect following and no endpoint fallback: text-to-image
/// posts JSON to <c>/v1/images/generations</c>, image-to-image posts multipart to
/// <c>/v1/images/edits</c>. Every failure is normalized into a
/// <see cref="ProviderFailureException"/> so the worker can record a precise <c>provider_*</c> code
/// while keeping the task <c>Unknown</c>.
/// </summary>
/// <remarks>
/// Logging contract (one structured line per event, never string-concatenated):
/// <list type="bullet">
/// <item><c>provider.dispatch</c> — everything the upstream request will contain, before it is sent.</item>
/// <item><c>provider.success</c> — status, elapsed, image count and total bytes.</item>
/// <item><c>provider.failure</c> — status, elapsed, error kind and a 200 character body snippet.</item>
/// </list>
/// The API key is never logged, and the prompt is truncated to 60 characters.
/// </remarks>
public sealed class OpenAiCompatibleImageProvider : IImageProvider
{
    /// <summary>Provider identifier used by the registry and by persisted tasks.</summary>
    public const string ProviderId = ProviderIds.OpenAiImages;

    /// <summary>Upper bound for an image response body (32 MiB).</summary>
    public const int MaxResponseBytes = 33_554_432;

    /// <summary>Timeout of the follow-up download when the upstream answers with a URL.</summary>
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Deadline of the generation POST (also enforced by the named HTTP client).</summary>
    public static readonly TimeSpan GenerationTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Path used when the request carries no reference image (JSON body).</summary>
    public const string GenerationsPath = "/v1/images/generations";

    /// <summary>Path used when the request carries at least one reference image (multipart body).</summary>
    public const string EditsPath = "/v1/images/edits";

    private const string TransportJson = "json";
    private const string TransportMultipart = "multipart";

    /// <summary>Maximum number of prompt characters written to the log.</summary>
    private const int PromptPreviewLength = 60;

    /// <summary>Maximum number of error-body characters written to the log.</summary>
    private const int ErrorSnippetLogLength = 200;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiCompatibleImageProvider> _logger;

    public OpenAiCompatibleImageProvider(IHttpClientFactory httpClientFactory,
        ILogger<OpenAiCompatibleImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => ProviderId;

    /// <inheritdoc />
    public bool RequiresConnection => true;

    /// <inheritdoc />
    public async Task<IReadOnlyList<GeneratedImage>> GenerateAsync(ProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = request.Connection
                         ?? throw new InvalidOperationException("provider_connection_required");
        // No Base URL means no configured relay: refuse before any HttpClient exists, so an
        // unconfigured profile can never produce an outbound request (and never ship the stored key
        // to a substituted host). ProviderUrl.Normalize has no default address either.
        if (string.IsNullOrWhiteSpace(connection.BaseUrl))
            throw new InvalidOperationException("provider_base_url_required");

        var normalized = ProviderUrl.Normalize(connection.BaseUrl);

        var options = request.EffectiveOptions;
        var referenceCount = request.EffectiveInputImages.Count;
        var transport = referenceCount > 0 ? TransportMultipart : TransportJson;
        var path = referenceCount > 0 ? EditsPath : GenerationsPath;

        _logger.LogInformation(
            "provider.dispatch taskId={TaskId} attemptId={AttemptId} provider={Provider} model={Model} " +
            "transport={Transport} path={Path} size={Size} count={Count} quality={Quality} format={Format} " +
            "transparent={Transparent} moderation={Moderation} references={References} prompt={Prompt}",
            request.TaskId, request.AttemptId, ProviderId, request.Model,
            transport, path, DescribeSize(request.Resolution), options.Count,
            options.Quality ?? "default", options.Format ?? "default", options.Transparent,
            options.Moderation ?? "default", referenceCount, Preview(request.Prompt.Body));

        var call = new ProviderCall();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient(InfrastructureRegistration.OpenAiHttpClient);
            // URL payloads are fetched through the guarded client: its ConnectCallback re-resolves
            // the host and only connects to an address that passed the public-address check.
            var downloadClient =
                _httpClientFactory.CreateClient(InfrastructureRegistration.OpenAiDownloadHttpClient);
            var images = await RequestImagesAsync(client, downloadClient, normalized, connection, request,
                call, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (images.Count == 0)
            {
                // An empty batch means we cannot prove an image was produced.
                LogFailure(request, call, ProviderErrorKind.InvalidResponse, stopwatch.ElapsedMilliseconds,
                    "empty image batch");
                throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
            }

            var totalBytes = 0;
            foreach (var image in images) totalBytes += image.Bytes.Length;

            _logger.LogInformation(
                "provider.success taskId={TaskId} attemptId={AttemptId} status={Status} elapsedMs={ElapsedMs} " +
                "images={Images} bytes={Bytes}",
                request.TaskId, request.AttemptId, call.Status ?? 0, stopwatch.ElapsedMilliseconds,
                images.Count, totalBytes);
            return images;
        }
        catch (ProviderFailureException ex)
        {
            stopwatch.Stop();
            LogFailure(request, call, ex.Kind, stopwatch.ElapsedMilliseconds, call.Snippet);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            LogFailure(request, call, ProviderErrorKind.Network, stopwatch.ElapsedMilliseconds, "timeout");
            throw new ProviderFailureException(ProviderErrorKind.Network);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            LogFailure(request, call, ProviderErrorKind.Network, stopwatch.ElapsedMilliseconds, ex.Message);
            throw new ProviderFailureException(ProviderErrorKind.Network);
        }
        catch (IOException ex)
        {
            stopwatch.Stop();
            LogFailure(request, call, ProviderErrorKind.Network, stopwatch.ElapsedMilliseconds, ex.Message);
            throw new ProviderFailureException(ProviderErrorKind.Network);
        }
    }

    /// <summary>Writes the single structured failure line; never includes the API key.</summary>
    private void LogFailure(ProviderRequest request, ProviderCall call, ProviderErrorKind kind,
        long elapsedMs, string? errorSnippet)
    {
        _logger.LogWarning(
            "provider.failure taskId={TaskId} attemptId={AttemptId} stage={Stage} status={Status} " +
            "elapsedMs={ElapsedMs} errorKind={ErrorKind} errorSnippet={ErrorSnippet}",
            request.TaskId, request.AttemptId, call.Stage, call.Status, elapsedMs, ProviderErrorCodes.For(kind),
            Snippet(errorSnippet));
    }

    private async Task<IReadOnlyList<GeneratedImage>> RequestImagesAsync(HttpClient client,
        HttpClient downloadClient, NormalizedBaseUrl normalized, ProviderConnection connection,
        ProviderRequest request, ProviderCall call, CancellationToken cancellationToken)
    {
        // The presence of reference images is the only switch: there is deliberately no fallback from
        // /edits back to /generations when the upstream rejects the edit request.
        var hasInputImages = request.EffectiveInputImages.Count > 0;
        var url = hasInputImages ? normalized.EditsUrl : normalized.ImagesUrl;

        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + connection.ApiKey);
        message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
        message.Headers.TryAddWithoutValidation("Accept", "application/json");
        message.Content = hasInputImages
            ? OpenAiCompatibleProtocol.BuildMultipartBody(request)
            : new StringContent(OpenAiCompatibleProtocol.BuildRequestBody(request), Encoding.UTF8,
                "application/json");

        call.Stage = "generation";
        using var response = await client
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var status = (int)response.StatusCode;
        call.Status = status;
        if (!response.IsSuccessStatusCode)
        {
            var snippet = await ReadSnippetAsync(response.Content, OpenAiCompatibleProtocol.MaxErrorSnippetBytes,
                cancellationToken).ConfigureAwait(false);
            call.Snippet = snippet;
            throw new ProviderFailureException(OpenAiCompatibleProtocol.Classify(status, snippet));
        }

        var body = await ReadLimitedAsync(response.Content, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);
        call.Stage = "parse";
        var raws = OpenAiCompatibleProtocol.ParseImages(Encoding.UTF8.GetString(body));

        // Parse and verify the entire batch before returning: a failure on entry N must not leave
        // entries 1..N-1 behind.
        var images = new List<GeneratedImage>(raws.Count);
        call.Stage = "download";
        foreach (var raw in raws)
        {
            var bytes = await ResolveBytesAsync(downloadClient, raw, cancellationToken).ConfigureAwait(false);
            if (!ImageProbe.TryRead(bytes, out var width, out var height, out var mimeType))
            {
                call.Snippet = $"image probe failed, {bytes.Length} bytes";
                throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
            }

            images.Add(new GeneratedImage(bytes, mimeType, width, height));
        }

        return images;
    }

    private static async Task<byte[]> ResolveBytesAsync(HttpClient downloadClient, RawImage raw,
        CancellationToken cancellationToken)
    {
        if (raw.Base64 is not null)
        {
            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(raw.Base64);
            }
            catch (FormatException)
            {
                throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
            }

            if (decoded.Length == 0) throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
            return decoded;
        }

        return await DownloadAsync(downloadClient, raw.Url!, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> DownloadAsync(HttpClient client, string url,
        CancellationToken cancellationToken)
    {
        // Only https, never loopback or private ranges: the relay chooses this URL, not the user.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || ProviderUrl.IsRestrictedHost(uri))
            throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.TryAddWithoutValidation("Accept", "image/*");
            using var response = await client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);

            var bytes = await ReadLimitedAsync(response.Content, MaxResponseBytes, timeout.Token)
                .ConfigureAwait(false);
            if (bytes.Length == 0) throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
            return bytes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
        }
        catch (HttpRequestException)
        {
            throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
        }
        catch (IOException)
        {
            throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes)
                throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task<string> ReadSnippetAsync(HttpContent content, int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[maxBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// <summary>The <c>size</c> value that will be sent, or <c>auto</c> when it is omitted.</summary>
    private static string DescribeSize(ResolutionSnapshot? resolution)
    {
        if (resolution is null || resolution.Mode == ResolutionMode.Auto) return "auto";
        try
        {
            return ResolutionSizing.SizeString(resolution);
        }
        catch (DomainException)
        {
            return "invalid";
        }
    }

    /// <summary>Truncates the prompt to a fixed prefix so the log line stays readable.</summary>
    private static string Preview(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "-";
        var single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= PromptPreviewLength ? single : single[..PromptPreviewLength] + "…";
    }

    /// <summary>Collapses newlines and truncates an upstream body to one readable log value.</summary>
    private static string Snippet(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "-";
        var single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= ErrorSnippetLogLength ? single : single[..ErrorSnippetLogLength] + "…";
    }

    /// <summary>Mutable facts about the in-flight request, used by the single failure line.</summary>
    private sealed class ProviderCall
    {
        /// <summary>HTTP status of the generation response once headers were received.</summary>
        public int? Status { get; set; }

        /// <summary>First bytes of the failing response body, or a diagnostic of the failure cause.</summary>
        public string? Snippet { get; set; }

        /// <summary>Which phase failed: <c>generation</c>, <c>parse</c> or <c>download</c>.</summary>
        public string Stage { get; set; } = "dispatch";
    }
}
