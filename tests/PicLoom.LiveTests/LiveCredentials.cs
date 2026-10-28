using Microsoft.Data.Sqlite;
using Xunit;
using PicLoom.Infrastructure;
using PicLoom.Infrastructure.Security;

namespace PicLoom.LiveTests;

/// <summary>
/// Credentials for one live run. There is no synthetic fallback: when neither source yields a
/// complete set (Base URL + key + model) the live tests are skipped instead of faked.
/// </summary>
/// <param name="BaseUrl">Relay Base URL exactly as the app would store it.</param>
/// <param name="ApiKey">Plaintext secret, only ever held in memory for this process.</param>
/// <param name="Model">Model name sent upstream.</param>
/// <param name="Source">Human readable origin, printed in the test output.</param>
internal sealed record LiveCredentials(string BaseUrl, string ApiKey, string Model, string Source)
{
    /// <summary>Masked key used by every diagnostic string; the tail is never longer than 4 chars.</summary>
    public string MaskedKey => ApiKey.Length <= 8 ? "sk-…" : "sk-…" + ApiKey[^4..];
}

/// <summary>
/// Resolves live credentials, in order: environment variables, then the BYOK configuration already
/// saved on this machine (SQLite settings row + DPAPI secret of the current Windows user).
/// </summary>
/// <remarks>
/// Required environment variables: <c>PICLOOM_LIVE_BASE_URL</c>, <c>PICLOOM_LIVE_API_KEY</c> and
/// <c>PICLOOM_LIVE_MODEL</c>. The saved-BYK path reads
/// <c>%LOCALAPPDATA%\PicLoom\picloom.db</c> plus <c>%LOCALAPPDATA%\PicLoom\secrets</c> read-only, so
/// running the tests never mutates the user's real data directory.
/// </remarks>
internal static class LiveCredentialsResolver
{
    /// <summary>Environment variable holding the relay Base URL.</summary>
    public const string BaseUrlVariable = "PICLOOM_LIVE_BASE_URL";

    /// <summary>Environment variable holding the relay API key.</summary>
    public const string ApiKeyVariable = "PICLOOM_LIVE_API_KEY";

    /// <summary>Environment variable holding the model name.</summary>
    public const string ModelVariable = "PICLOOM_LIVE_MODEL";

    private static readonly Lazy<LiveCredentials?> Resolver =
        new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The resolved credentials, or null when this machine cannot run live tests.</summary>
    public static LiveCredentials? Current => Resolver.Value;

    /// <summary>True when a real upstream call can be made.</summary>
    public static bool IsAvailable => Current is not null;

    /// <summary>Why live tests are skipped, plus how to make them run.</summary>
    public static string SkipReason =>
        string.Join(" ", [
            "Live upstream tests skipped: no credentials found.",
            "Set " + BaseUrlVariable + " / " + ApiKeyVariable + " / " + ModelVariable + ",",
            "or save a BYOK configuration in the app (Settings -> API connection) so that",
            @"%LOCALAPPDATA%\PicLoom holds a key decryptable by the current Windows user.",
            "These three tests make three real upstream calls, so they are opt-in."
        ]);

    private static LiveCredentials? Resolve()
    {
        var fromEnvironment = Environment();
        if (fromEnvironment is not null) return fromEnvironment;
        return FromSavedSettings();
    }

    private static LiveCredentials? Environment()
    {
        var baseUrl = System.Environment.GetEnvironmentVariable(BaseUrlVariable);
        var apiKey = System.Environment.GetEnvironmentVariable(ApiKeyVariable);
        var model = System.Environment.GetEnvironmentVariable(ModelVariable);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(model))
            return null;

        return new LiveCredentials(baseUrl.Trim(), apiKey.Trim(), model.Trim(),
            $"env({BaseUrlVariable}/{ApiKeyVariable}/{ModelVariable})");
    }

    /// <summary>
    /// Reads the settings row and the DPAPI secret from the real data directory. Any failure means
    /// "cannot run here", never "test failed": the defaults for this machine may simply not be set up.
    /// </summary>
    private static LiveCredentials? FromSavedSettings()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var dataDirectory = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PicLoom");
            var databasePath = System.IO.Path.Combine(dataDirectory, "picloom.db");
            if (!File.Exists(databasePath)) return null;

            var (baseUrl, model, keyOrigin, hasKey) = ReadSettingsRow(databasePath);
            if (!hasKey || string.IsNullOrWhiteSpace(baseUrl)) return null;

            var origin = string.IsNullOrWhiteSpace(keyOrigin) ? OriginOf(baseUrl) : keyOrigin;
            if (string.IsNullOrWhiteSpace(origin)) return null;

            var store = new DpapiSecretStore(new PicLoomPaths(dataDirectory));
            var secret = store.ReadAsync(origin, CancellationToken.None).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(secret)) return null;

            return new LiveCredentials(baseUrl, secret,
                string.IsNullOrWhiteSpace(model) ? "gpt-image-1" : model,
                "saved BYOK (" + dataDirectory + ", keyOrigin=" + origin + ")");
        }
        catch (Exception)
        {
            // Unreadable / encrypted for another user / locked: treat as "not configured here".
            return null;
        }
    }

    /// <summary>
    /// Reads the single settings row. Strictly read-only: the first attempt opens the database with
    /// <see cref="SqliteOpenMode.ReadOnly"/> and <see cref="SqliteCacheMode.Shared"/>, which is enough
    /// when the app already created the <c>-shm</c>/<c>-wal</c> files. A WAL database that has no
    /// <c>-shm</c> file cannot be opened read-only, so a documented fallback opens it read-write;
    /// that fallback still issues nothing but this one <c>SELECT</c> and never creates a table, a row
    /// or a checkpoint.
    /// </summary>
    private static (string BaseUrl, string Model, string KeyOrigin, bool HasKey) ReadSettingsRow(
        string databasePath)
    {
        try
        {
            return Query(BuildConnectionString(databasePath, SqliteOpenMode.ReadOnly, SqliteCacheMode.Shared));
        }
        catch (SqliteException)
        {
            // ReadOnly failed (WAL needs a writable -shm): retry read-write, SELECT only.
            return Query(BuildConnectionString(databasePath, SqliteOpenMode.ReadWrite, SqliteCacheMode.Default));
        }
    }

    private static string BuildConnectionString(string databasePath, SqliteOpenMode mode,
        SqliteCacheMode cache) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = cache,
            // A private connection keeps the user's pooled connections untouched.
            Pooling = false
        }.ToString();

    private static (string BaseUrl, string Model, string KeyOrigin, bool HasKey) Query(
        string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"BaseUrl\", \"Model\", \"KeyOrigin\", \"HasKey\" FROM \"ProviderSettings\" " +
            "WHERE \"Protocol\" = 'OpenAiImages' LIMIT 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return ("", "", "", false);
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0);
    }

    private static string? OriginOf(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant()
            : null;
}

/// <summary>
/// A <see cref="FactAttribute"/> that reports itself as skipped (never failed) when no live
/// credentials exist. Keeps <c>dotnet test</c> green on a machine without a relay configuration
/// while still running the three real upstream calls where one is configured.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (!LiveCredentialsResolver.IsAvailable) Skip = LiveCredentialsResolver.SkipReason;
    }
}
