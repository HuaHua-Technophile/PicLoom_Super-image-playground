namespace PicLoom.Core;

/// <summary>
/// The five generation knobs accepted by <c>POST /api/tasks</c>. Every member is optional so the
/// legacy body (prompt + resolution only) keeps binding unchanged.
/// </summary>
/// <param name="Quality">One of <c>low</c>, <c>medium</c>, <c>high</c>, or null for "let the model decide".</param>
/// <param name="Count">Images requested per dispatch, 1..4; null means 1.</param>
/// <param name="Format">One of <c>png</c>, <c>jpeg</c>, <c>webp</c>, or null for the upstream default.</param>
/// <param name="Transparent">Requests a transparent background; only valid for png/webp.</param>
/// <param name="Moderation">Only <c>low</c> is supported today, or null for the upstream default.</param>
public sealed record GenerationOptionsInput(string? Quality = null, int? Count = null,
    string? Format = null, bool Transparent = false, string? Moderation = null);

/// <summary>
/// Immutable, validated generation options persisted with the task and forwarded to the provider.
/// A field whose value is "not chosen by the user" is <c>null</c> and must never appear in the
/// upstream request body.
/// </summary>
public sealed record GenerationOptions(string? Quality, int Count, string? Format, bool Transparent,
    string? Moderation)
{
    /// <summary>Lowest accepted value for <see cref="Count"/>.</summary>
    public const int MinCount = 1;

    /// <summary>Highest accepted value for <see cref="Count"/>.</summary>
    public const int MaxCount = 4;

    /// <summary>Default image count when the request omits it.</summary>
    public const int DefaultCount = 1;

    /// <summary>Literal emitted for <c>background</c> when transparency is requested.</summary>
    public const string TransparentBackground = "transparent";

    /// <summary>Value used for the <c>jpeg</c> output format.</summary>
    public const string FormatJpeg = "jpeg";

    /// <summary>Default options: nothing is sent upstream.</summary>
    public static readonly GenerationOptions Default = new(null, DefaultCount, null, false, null);

    private static readonly string[] Qualities = ["low", "medium", "high"];
    private static readonly string[] Formats = ["png", "jpeg", "webp"];
    private static readonly string[] Moderations = ["low"];

    /// <summary>
    /// Validates and normalises the wire input. Every rule is local: an invalid combination is
    /// rejected before a task row exists and before any upstream request can happen.
    /// </summary>
    public static GenerationOptions Normalize(GenerationOptionsInput? input)
    {
        if (input is null) return Default;

        var quality = NormalizeOptional(input.Quality, Qualities, "options_invalid");
        var format = NormalizeOptional(input.Format, Formats, "options_invalid");
        var moderation = NormalizeOptional(input.Moderation, Moderations, "options_invalid");

        if (input.Count is { } requested && (requested < MinCount || requested > MaxCount))
            throw new DomainException("options_count_invalid");

        var count = input.Count ?? DefaultCount;
        var transparent = input.Transparent;

        // Only PNG and WebP carry an alpha channel upstream; a null format is treated as PNG.
        if (transparent && string.Equals(format, FormatJpeg, StringComparison.Ordinal))
            throw new DomainException("options_invalid");

        return new GenerationOptions(quality, count, format, transparent, moderation);
    }

    /// <summary>True when no option would be sent upstream.</summary>
    public bool IsDefault => this == Default;

    private static string? NormalizeOptional(string? value, string[] allowed, string errorCode)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        var lowered = trimmed.ToLowerInvariant();
        if (!allowed.Contains(lowered, StringComparer.Ordinal))
            throw new DomainException(errorCode);
        return lowered;
    }
}
