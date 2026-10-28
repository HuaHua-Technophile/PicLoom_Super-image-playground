using System.Text.Json.Serialization;

namespace PicLoom.Core;

/// <summary>Hard limits shared by the API, the upload endpoints and the provider adapters.</summary>
public static class ReferenceRules
{
    /// <summary>Largest accepted reference image, in bytes (10 MiB).</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>Largest number of reference images bound to one task.</summary>
    public const int MaxPerTask = 4;

    /// <summary>
    /// Maximum accepted request body for the multipart upload endpoint. Slightly above
    /// <see cref="MaxBytes"/> so an oversized file surfaces as <c>reference_too_large</c> rather than
    /// a framework-level 413.
    /// </summary>
    public const int MaxRequestBodyBytes = 16 * 1024 * 1024;

    /// <summary>Largest accepted reference image edge, in pixels.</summary>
    public const int MaxEdge = 8192;
}

/// <summary>Where a reference image came from.</summary>
public static class ReferenceSources
{
    /// <summary>Uploaded from the local disk.</summary>
    public const string Upload = "upload";

    /// <summary>Fetched from an https URL supplied by the user.</summary>
    public const string Url = "url";
}

/// <summary>
/// A user supplied reference image (image-to-image input). A row is created unbound
/// (<see cref="TaskId"/> null) and is bound to exactly one task when that task is created.
/// </summary>
public sealed class ReferenceImage
{
    private ReferenceImage()
    {
    }

    public ReferenceImage(Guid id, string relativePath, string thumbnailPath, string mimeType, int width,
        int height, string source)
    {
        Id = id;
        RelativePath = relativePath;
        ThumbnailPath = thumbnailPath;
        MimeType = mimeType;
        Width = width;
        Height = height;
        Source = source;
    }

    public Guid Id { get; private set; }

    /// <summary>Null while the image is still a draft; set once a task consumes it.</summary>
    public Guid? TaskId { get; private set; }

    /// <summary>Server-side storage path; never serialized to the browser.</summary>
    [JsonIgnore]
    public string RelativePath { get; private set; } = "";

    /// <summary>Server-side thumbnail path; never serialized to the browser.</summary>
    [JsonIgnore]
    public string ThumbnailPath { get; private set; } = "";
    public string MimeType { get; private set; } = "";
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary><see cref="ReferenceSources.Upload"/> or <see cref="ReferenceSources.Url"/>.</summary>
    public string Source { get; private set; } = ReferenceSources.Upload;

    /// <summary>Position inside the owning task, starting at zero.</summary>
    public int Ordinal { get; private set; }

    /// <summary>Authenticated content endpoint for this image (consumed by the web UI).</summary>
    public string ContentUrl => $"/api/references/{Id:D}/content";

    /// <summary>Authenticated thumbnail endpoint for this image (consumed by the web UI).</summary>
    public string ThumbnailUrl => $"/api/references/{Id:D}/thumbnail";

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>Binds this image to a task. A second bind is a programming error, not a user error.</summary>
    public void BindTo(Guid taskId, int ordinal)
    {
        if (TaskId is not null) throw new DomainException("reference_in_use");
        TaskId = taskId;
        Ordinal = ordinal;
    }
}

/// <summary>Public projection of a reference image; paths stay server-side.</summary>
public sealed record ReferenceImageView(Guid Id, int Width, int Height, string MimeType, string ContentUrl,
    string ThumbnailUrl)
{
    /// <summary>Builds the view for a stored row.</summary>
    public static ReferenceImageView From(ReferenceImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new ReferenceImageView(image.Id, image.Width, image.Height, image.MimeType,
            image.ContentUrl, image.ThumbnailUrl);
    }
}

/// <summary>File name handed to the upstream multipart request (<c>input-1.png</c>, ...).</summary>
public static class ReferenceFileNames
{
    /// <summary>Builds the ordinal file name for the image at <paramref name="zeroBasedIndex"/>.</summary>
    public static string For(int zeroBasedIndex, string mimeType)
    {
        var extension = mimeType switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };
        return $"input-{zeroBasedIndex + 1}{extension}";
    }
}

/// <summary>
/// Validation for user supplied reference URLs. Only https is accepted and loopback, private,
/// link-local and unique-local hosts are refused before any socket is opened.
/// </summary>
public static class ReferenceUrl
{
    /// <summary>Parses and validates the URL, or throws <c>reference_url_invalid</c>.</summary>
    public static Uri Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new DomainException("reference_url_invalid");

        var raw = url.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("reference_url_invalid");

        if (ProviderUrl.IsRestrictedHost(uri)) throw new DomainException("reference_url_invalid");
        return uri;
    }
}
