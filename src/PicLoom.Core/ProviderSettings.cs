using System.Text.Json.Serialization;

namespace PicLoom.Core;

/// <summary>Upstream wire protocols supported by the BYOK settings row.</summary>
public enum ProviderProtocol
{
    [JsonStringEnumMemberName("openaiImages")] OpenAiImages
}

/// <summary>Stable provider identifiers shared by the registry, the worker and the API.</summary>
public static class ProviderIds
{
    /// <summary>OpenAI compatible BYOK provider (the only adapter).</summary>
    public const string OpenAiImages = "openai-images";

    /// <summary>Built-in real model names offered by the UI; any other text is a custom relay alias.</summary>
    public static readonly IReadOnlyList<string> BuiltInModels = ["gpt-image-1", "gpt-image-2"];
}

/// <summary>
/// The single BYOK settings row (one row per <see cref="ProviderProtocol"/>). Only the masked key
/// is ever materialized here; the plaintext secret lives exclusively in the OS secret store.
/// </summary>
public sealed class ProviderSettings
{
    /// <summary>Default display name shown in the settings card.</summary>
    public const string DefaultDisplayName = "OpenAI 兼容（BYOK）";

    private ProviderSettings() { }

    public ProviderSettings(ProviderProtocol protocol, string displayName)
    {
        Protocol = protocol;
        DisplayName = DomainRule.Text(displayName, 120);
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Persisted as a string so the protocol stays readable in SQLite.</summary>
    public ProviderProtocol Protocol { get; private set; }

    public string DisplayName { get; private set; } = "";

    /// <summary>Normalized absolute base URL (may carry a path, never a trailing <c>/v1</c>).</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>Remembered default model name; an empty string means "not chosen yet".</summary>
    public string Model { get; private set; } = "";

    /// <summary>Secret-store key of the stored key, i.e. <c>scheme://host[:port]</c>.</summary>
    public string KeyOrigin { get; private set; } = "";

    /// <summary>Pre-rendered mask such as <c>sk-…a1b2</c>; null when the secret is short or absent.</summary>
    public string? MaskedKey { get; private set; }

    /// <summary>True when a secret file is expected to exist in the secret store.</summary>
    public bool HasKey { get; private set; }

    public bool IsEnabled { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Applies a validated settings update.</summary>
    public void Apply(string baseUrl, string model, bool enabled, string keyOrigin, string? maskedKey, bool hasKey)
    {
        BaseUrl = baseUrl;
        Model = model;
        IsEnabled = enabled;
        KeyOrigin = keyOrigin;
        MaskedKey = maskedKey;
        HasKey = hasKey;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Forgets the secret while keeping the remembered Base URL and model.</summary>
    public void Forget()
    {
        KeyOrigin = "";
        MaskedKey = null;
        HasKey = false;
        IsEnabled = false;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>Body of <c>PUT /api/settings/provider</c>.</summary>
/// <param name="BaseUrl">Required on first save; normalized before persistence.</param>
/// <param name="ApiKey">Plaintext key. Null or empty means "keep the stored one" (same origin only).</param>
/// <param name="Model">Remembered default model name.</param>
/// <param name="Enabled">Whether inline tasks may use this configuration.</param>
public sealed record ProviderSettingsInput(string? BaseUrl, string? ApiKey, string? Model, bool? Enabled);

/// <summary>Response body of <c>GET/PUT /api/settings/provider</c>. Never carries the plaintext key.</summary>
public sealed record ProviderSettingsView(ProviderProtocol Protocol, string? BaseUrl, string? Model,
    bool Configured, bool Enabled, string? MaskedKey, DateTime? UpdatedAt);

/// <summary>Three-state outcome of the connection probe.</summary>
public enum ProviderTestOutcome
{
    /// <summary>2xx and the body parsed as JSON.</summary>
    [JsonStringEnumMemberName("ok")] Ok,

    /// <summary>401 or 403: the key was rejected.</summary>
    [JsonStringEnumMemberName("unauthorized")] Unauthorized,

    /// <summary>Cannot be decided without generating an image (404/405/5xx/timeout/network/not JSON).</summary>
    [JsonStringEnumMemberName("unknown")] Unknown
}

/// <summary>Three-state result of the connection probe.</summary>
/// <param name="Result">One of <c>ok</c>, <c>unauthorized</c>, <c>unknown</c>.</param>
/// <param name="Status">The upstream HTTP status when a response was received.</param>
public sealed record ProviderTestResult(ProviderTestOutcome Result, int? Status);

/// <summary>In-memory credentials handed to a provider adapter; never persisted or logged.</summary>
public sealed record ProviderConnection(string BaseUrl, string ApiKey);

/// <summary>Masking rules for the API key. Only ever renders the trailing four characters.</summary>
public static class ProviderKeyMask
{
    /// <summary>Minimum secret length required before a mask is shown.</summary>
    public const int MinimumMaskableLength = 12;

    /// <summary>Returns <c>sk-…abcd</c> style mask, or null when the secret is too short to mask.</summary>
    public static string? Mask(string? secret)
    {
        if (secret is null || secret.Length < MinimumMaskableLength) return null;
        return "sk-…" + secret[^4..];
    }
}
