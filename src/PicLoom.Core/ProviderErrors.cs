namespace PicLoom.Core;

/// <summary>
/// Eight upstream failure categories. A dispatched task always ends in <c>Unknown</c>; only the
/// recorded <c>ErrorCode</c> reflects the category.
/// </summary>
public enum ProviderErrorKind
{
    Unauthorized,
    ModelNotFound,
    SizeRejected,
    RateLimited,
    InsufficientQuota,
    Network,
    UpstreamError,
    InvalidResponse
}

/// <summary>Maps a <see cref="ProviderErrorKind"/> to the persisted <c>ErrorCode</c> value.</summary>
public static class ProviderErrorCodes
{
    public const string Unauthorized = "provider_unauthorized";
    public const string ModelNotFound = "provider_model_not_found";
    public const string SizeRejected = "provider_size_rejected";
    public const string RateLimited = "provider_rate_limited";
    public const string InsufficientQuota = "provider_insufficient_quota";
    public const string Network = "provider_network";
    public const string UpstreamError = "provider_upstream_error";
    public const string InvalidResponse = "provider_invalid_response";

    /// <summary>Returns the stable snake_case code for a failure category.</summary>
    public static string For(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.Unauthorized => Unauthorized,
        ProviderErrorKind.ModelNotFound => ModelNotFound,
        ProviderErrorKind.SizeRejected => SizeRejected,
        ProviderErrorKind.RateLimited => RateLimited,
        ProviderErrorKind.InsufficientQuota => InsufficientQuota,
        ProviderErrorKind.Network => Network,
        ProviderErrorKind.UpstreamError => UpstreamError,
        ProviderErrorKind.InvalidResponse => InvalidResponse,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown_provider_error_kind")
    };
}

/// <summary>
/// Raised by a real provider adapter when the upstream call failed in a recognisable way. The
/// worker records <see cref="Code"/> and leaves the task <c>Unknown</c> (never re-dispatched).
/// </summary>
public sealed class ProviderFailureException : Exception
{
    public ProviderFailureException(ProviderErrorKind kind)
        : base(ProviderErrorCodes.For(kind))
    {
        Kind = kind;
    }

    /// <summary>The failure category.</summary>
    public ProviderErrorKind Kind { get; }

    /// <summary>The stable error code persisted on the task and the attempt.</summary>
    public string Code => ProviderErrorCodes.For(Kind);
}
