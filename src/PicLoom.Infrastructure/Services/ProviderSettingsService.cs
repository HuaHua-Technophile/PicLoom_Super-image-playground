using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PicLoom.Core;
using PicLoom.Infrastructure.Persistence;

namespace PicLoom.Infrastructure.Services;

/// <summary>
/// Reads and writes the single BYOK settings row and resolves the in-memory credentials used for
/// dispatch. The plaintext key only ever lives in the OS secret store, inside a
/// <see cref="ProviderConnection"/> or in the outbound <c>Authorization</c> header; it is never
/// persisted, logged or returned.
/// </summary>
public sealed class ProviderSettingsService : IProviderSettingsService, IProviderCredentials
{
    /// <summary>Upper bound for the <c>/v1/models</c> probe body (1 MiB).</summary>
    public const int MaxModelsBodyBytes = 1_048_576;

    /// <summary>Total timeout of the connection probe, including connect and body read.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly ProviderProtocol Protocol = ProviderProtocol.OpenAiImages;

    private readonly PicLoomDbContext _db;
    private readonly ISecretStore _secrets;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProviderSettingsService> _logger;

    public ProviderSettingsService(PicLoomDbContext db, ISecretStore secrets,
        IHttpClientFactory httpClientFactory, ILogger<ProviderSettingsService> logger)
    {
        _db = db;
        _secrets = secrets;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProviderSettingsView> GetAsync(CancellationToken cancellationToken)
    {
        var row = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return ToView(row);
    }

    /// <inheritdoc />
    public async Task<ProviderSettingsView> UpsertAsync(ProviderSettingsInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var normalized = ProviderUrl.Normalize(input.BaseUrl);
        var apiKey = string.IsNullOrWhiteSpace(input.ApiKey) ? null : input.ApiKey.Trim();
        var model = ValidateModel(input.Model);
        var enabled = input.Enabled ?? true;

        var row = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            // First save always needs a key; otherwise there would be nothing to protect.
            if (apiKey is null) throw new DomainException("secret_required");

            await _secrets.SaveAsync(normalized.Origin, apiKey, cancellationToken).ConfigureAwait(false);
            var created = new ProviderSettings(Protocol, ProviderSettings.DefaultDisplayName);
            created.Apply(normalized.BaseUrl, model, enabled, normalized.Origin,
                ProviderKeyMask.Mask(apiKey), hasKey: true);
            _db.ProviderSettings.Add(created);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("provider_settings_saved created=true enabled={Enabled}", enabled);
            return ToView(created);
        }

        var originChanged = !string.Equals(row.KeyOrigin, normalized.Origin, StringComparison.OrdinalIgnoreCase);
        if (originChanged && apiKey is null)
        {
            // "Empty key means unchanged" only holds inside one origin: carrying a key to another
            // host would silently ship the credential somewhere the user did not choose.
            throw new DomainException("provider_key_required_for_new_origin");
        }

        if (apiKey is null)
        {
            // Same origin, no new key: keep the stored secret and mask, only refresh URL/model.
            row.Apply(normalized.BaseUrl, model, enabled, row.KeyOrigin, row.MaskedKey, row.HasKey);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("provider_settings_saved created=false enabled={Enabled} keyChanged=false", enabled);
            return ToView(row);
        }

        await _secrets.SaveAsync(normalized.Origin, apiKey, cancellationToken).ConfigureAwait(false);
        if (originChanged && !string.IsNullOrEmpty(row.KeyOrigin))
            await DeleteSecretAsync(row.KeyOrigin, cancellationToken).ConfigureAwait(false);

        row.Apply(normalized.BaseUrl, model, enabled, normalized.Origin,
            ProviderKeyMask.Mask(apiKey), hasKey: true);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("provider_settings_saved created=false enabled={Enabled} keyChanged=true originChanged={OriginChanged}",
            enabled, originChanged);
        return ToView(row);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var row = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (row is null) return;

        var origin = row.KeyOrigin;
        if (!string.IsNullOrEmpty(origin))
            await DeleteSecretAsync(origin, cancellationToken).ConfigureAwait(false);

        // Keep BaseUrl and model so the user can re-enter just the key.
        row.Forget();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("provider_settings_cleared");
    }

    /// <inheritdoc />
    public async Task<ProviderTestResult> TestAsync(ProviderSettingsInput? input,
        CancellationToken cancellationToken)
    {
        var row = await LoadAsync(cancellationToken).ConfigureAwait(false);

        var requestedBaseUrl = string.IsNullOrWhiteSpace(input?.BaseUrl) ? null : input!.BaseUrl;
        var baseUrl = requestedBaseUrl ?? (string.IsNullOrWhiteSpace(row?.BaseUrl) ? null : row!.BaseUrl);
        if (baseUrl is null) throw new DomainException("base_url_required");

        var normalized = ProviderUrl.Normalize(baseUrl);

        var apiKey = string.IsNullOrWhiteSpace(input?.ApiKey) ? null : input!.ApiKey.Trim();
        if (apiKey is null
            && row is { HasKey: true }
            && string.Equals(row.KeyOrigin, normalized.Origin, StringComparison.OrdinalIgnoreCase))
        {
            // An omitted key means "test the stored configuration", whether the caller echoed the
            // saved Base URL back (the settings card always does) or sent no body at all. The origin
            // equality check above is what keeps a stored key from being sent to another host, so
            // do NOT add a "requestedBaseUrl is null" guard here: it silently downgrades every
            // probe from the settings card to an anonymous call and reports a valid key as rejected.
            apiKey = await TryReadSecretAsync(row.KeyOrigin, cancellationToken).ConfigureAwait(false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, normalized.ModelsUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);

            var client = _httpClientFactory.CreateClient(InfrastructureRegistration.OpenAiHttpClient);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogInformation("provider_test result=unauthorized status={Status}", status);
                return new ProviderTestResult(ProviderTestOutcome.Unauthorized, status);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("provider_test result=unknown status={Status}", status);
                return new ProviderTestResult(ProviderTestOutcome.Unknown, status);
            }

            var body = await ReadLimitedAsync(response.Content, MaxModelsBodyBytes, timeout.Token)
                .ConfigureAwait(false);
            var parseable = IsJson(body);
            _logger.LogInformation("provider_test result={Result} status={Status} length={Length}",
                parseable ? "ok" : "unknown", status, body.Length);
            return parseable
                ? new ProviderTestResult(ProviderTestOutcome.Ok, status)
                : new ProviderTestResult(ProviderTestOutcome.Unknown, status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Timeout, DNS or transport failure: the probe cannot decide either way.
            _logger.LogInformation("provider_test result=unknown status=null");
            return new ProviderTestResult(ProviderTestOutcome.Unknown, null);
        }
    }

    /// <inheritdoc />
    public async Task<ProviderConnection?> ResolveAsync(string providerId, CancellationToken cancellationToken)
    {
        if (!string.Equals(providerId, ProviderIds.OpenAiImages, StringComparison.Ordinal)) return null;

        var row = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (row is not { HasKey: true, IsEnabled: true }) return null;
        if (string.IsNullOrEmpty(row.KeyOrigin) || string.IsNullOrEmpty(row.BaseUrl)) return null;

        var secret = await TryReadSecretAsync(row.KeyOrigin, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secret)) return null;

        return new ProviderConnection(row.BaseUrl, secret);
    }

    private async Task<ProviderSettings?> LoadAsync(CancellationToken cancellationToken) =>
        await _db.ProviderSettings.FirstOrDefaultAsync(x => x.Protocol == Protocol, cancellationToken)
            .ConfigureAwait(false);

    private static ProviderSettingsView ToView(ProviderSettings? row)
    {
        if (row is null)
            return new ProviderSettingsView(Protocol, null, null, false, false, null, null);

        var configured = row.HasKey && !string.IsNullOrEmpty(row.KeyOrigin);
        return new ProviderSettingsView(Protocol,
            NullIfEmpty(row.BaseUrl),
            NullIfEmpty(row.Model),
            configured,
            row.IsEnabled,
            configured ? row.MaskedKey : null,
            row.UpdatedAt);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static string ValidateModel(string? model)
    {
        var value = model?.Trim() ?? "";
        if (value.Length > 120) throw new DomainException("validation_failed");
        return value;
    }

    private async Task<string?> TryReadSecretAsync(string origin, CancellationToken cancellationToken)
    {
        try
        {
            return await _secrets.ReadAsync(origin, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Unreadable secret (foreign user profile, corrupted blob) is treated as unconfigured.
            _logger.LogWarning("provider_secret_unreadable");
            return null;
        }
    }

    private async Task DeleteSecretAsync(string origin, CancellationToken cancellationToken)
    {
        try
        {
            await _secrets.DeleteAsync(origin, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Best effort: a leftover file is unreachable because the mask/origin were replaced.
            _logger.LogWarning("provider_secret_delete_failed");
        }
    }

    private static bool IsJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return false;

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(body);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpContent content, int maxBytes,
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

        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }
}
