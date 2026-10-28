using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicLoom.Core;

namespace PicLoom.Infrastructure.Providers;

/// <summary>The image payload extracted from a successful OpenAI compatible response.</summary>
/// <param name="Base64">Inline base64 payload when the upstream returns <c>b64_json</c>.</param>
/// <param name="Url">Remote URL when the upstream returns <c>url</c> instead.</param>
public sealed record RawImage(string? Base64, string? Url);

/// <summary>
/// Pure request/response translation for the OpenAI compatible image protocol. Kept free of network
/// I/O so it can be unit tested without a server. Text-to-image produces a JSON body; image-to-image
/// produces a multipart body for <c>/v1/images/edits</c> whose reference images arrive as repeated
/// <c>image[]</c> fields.
/// </summary>
public static class OpenAiCompatibleProtocol
{
    /// <summary>Number of leading error-body bytes inspected for keyword classification.</summary>
    public const int MaxErrorSnippetBytes = 8192;

    /// <summary>Multipart field name carrying one reference image.</summary>
    public const string InputImageField = "image[]";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds the JSON body for <c>POST /v1/images/generations</c>.
    /// </summary>
    /// <remarks>
    /// An <see cref="ResolutionMode.Auto"/> snapshot deliberately omits <c>size</c> entirely (the
    /// serializer drops the null member) so the model picks its own dimensions; sending an empty or
    /// placeholder value would constrain it. Every optional generation parameter follows the same
    /// rule: when the user did not choose it, the key is absent from the body, not empty.
    /// </remarks>
    public static string BuildRequestBody(ProviderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Prompt);
        ArgumentNullException.ThrowIfNull(request.Resolution);

        var options = request.EffectiveOptions;
        var size = request.Resolution.Mode == ResolutionMode.Auto
            ? null
            : ResolutionSizing.SizeString(request.Resolution);

        // "response_format" is deliberately omitted: gpt-image-1 rejects it.
        var body = new ImageRequestBody(request.Model, request.Prompt.Body, NFor(options), size,
            options.Quality, options.Format, options.Moderation,
            options.Transparent ? GenerationOptions.TransparentBackground : null);
        return JsonSerializer.Serialize(body, JsonOptions);
    }

    /// <summary>
    /// Builds the multipart body for <c>POST /v1/images/edits</c>. One <c>image[]</c> part per
    /// reference image, in the order they were submitted, with the real MIME type as Content-Type.
    /// </summary>
    public static MultipartFormDataContent BuildMultipartBody(ProviderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Prompt);
        ArgumentNullException.ThrowIfNull(request.Resolution);

        var options = request.EffectiveOptions;
        var images = request.EffectiveInputImages;
        if (images.Count == 0) throw new InvalidOperationException("provider_input_image_required");

        var form = new MultipartFormDataContent();
        form.Add(new StringContent(request.Model, Encoding.UTF8), "model");
        form.Add(new StringContent(request.Prompt.Body, Encoding.UTF8), "prompt");

        if (request.Resolution.Mode != ResolutionMode.Auto)
            form.Add(new StringContent(ResolutionSizing.SizeString(request.Resolution), Encoding.UTF8), "size");

        if (NFor(options) is { } count)
            form.Add(new StringContent(count.ToString(CultureInfo.InvariantCulture), Encoding.UTF8), "n");

        AddOptional(form, "quality", options.Quality);
        AddOptional(form, "output_format", options.Format);
        AddOptional(form, "moderation", options.Moderation);
        if (options.Transparent)
            form.Add(new StringContent(GenerationOptions.TransparentBackground, Encoding.UTF8), "background");

        foreach (var image in images)
        {
            var content = new ByteArrayContent(image.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(image.MimeType);
            form.Add(content, InputImageField, image.FileName);
        }

        return form;
    }

    /// <summary>Extracts every <c>data[].b64_json</c> or <c>data[].url</c> entry from a 2xx body.</summary>
    /// <remarks>
    /// Strict by design: one malformed entry, an empty array or a non-object payload fails the whole
    /// batch, so the worker can never persist a partial result.
    /// </remarks>
    public static IReadOnlyList<RawImage> ParseImages(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
                throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);

            var images = new List<RawImage>();
            foreach (var entry in data.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);

                string? base64 = null;
                string? url = null;

                if (entry.TryGetProperty("b64_json", out var base64Property)
                    && base64Property.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(base64Property.GetString()))
                {
                    base64 = base64Property.GetString();
                }
                else if (entry.TryGetProperty("url", out var urlProperty)
                         && urlProperty.ValueKind == JsonValueKind.String
                         && !string.IsNullOrWhiteSpace(urlProperty.GetString()))
                {
                    url = urlProperty.GetString();
                }

                if (base64 is null && url is null)
                    throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);

                images.Add(new RawImage(base64, url));
            }

            if (images.Count == 0) throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
            return images;
        }
        catch (JsonException)
        {
            throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);
        }
    }

    /// <summary>Extracts the first image of the batch; kept for callers that only need one.</summary>
    public static RawImage ParseImage(string json) => ParseImages(json)[0];

    /// <summary>
    /// Classifies a non-2xx response. Quota keywords win over the generic 400 branch because relays
    /// frequently answer "insufficient_quota" with a plain 400/429.
    /// </summary>
    public static ProviderErrorKind Classify(int statusCode, string? bodySnippet)
    {
        var snippet = bodySnippet ?? "";

        if (statusCode is 401 or 403) return ProviderErrorKind.Unauthorized;
        if (statusCode == 402 || ContainsAny(snippet, QuotaKeywords)) return ProviderErrorKind.InsufficientQuota;
        if (statusCode == 404 || ContainsAny(snippet, ModelKeywords)) return ProviderErrorKind.ModelNotFound;
        if (statusCode == 400 && ContainsAny(snippet, SizeKeywords)) return ProviderErrorKind.SizeRejected;
        if (statusCode == 429) return ProviderErrorKind.RateLimited;
        return ProviderErrorKind.UpstreamError;
    }

    private static readonly string[] QuotaKeywords = ["insufficient", "quota", "balance"];
    private static readonly string[] ModelKeywords = ["model not found", "model_not_found", "model_not_exist", "does not exist"];
    private static readonly string[] SizeKeywords = ["size", "dimension", "resolution"];

    private static bool ContainsAny(string text, string[] keywords)
    {
        if (text.Length == 0) return false;
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>The <c>n</c> member, present only when more than one image was requested.</summary>
    private static int? NFor(GenerationOptions options) =>
        options.Count > 1 ? options.Count : null;

    private static void AddOptional(MultipartFormDataContent form, string name, string? value)
    {
        if (value is null) return;
        form.Add(new StringContent(value, Encoding.UTF8), name);
    }

    /// <summary>
    /// Wire body. Every optional member is nullable so the <c>WhenWritingNull</c> policy can drop it
    /// instead of emitting an empty or constraining value.
    /// </summary>
    private sealed record ImageRequestBody(string Model, string Prompt, int? N, string? Size,
        string? Quality, [property: JsonPropertyName("output_format")] string? OutputFormat,
        string? Moderation, string? Background);
}
