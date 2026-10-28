using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PicLoom.Core;
using PicLoom.Infrastructure.Assets;
using PicLoom.Infrastructure.Generation;
using PicLoom.Infrastructure.Persistence;
using PicLoom.Infrastructure.Providers;
using PicLoom.Infrastructure.References;
using PicLoom.Infrastructure.Security;
using PicLoom.Infrastructure.Services;

namespace PicLoom.Infrastructure;

public static class InfrastructureRegistration
{
    /// <summary>Named HTTP client used for the OpenAI compatible image protocol.</summary>
    public const string OpenAiHttpClient = "picloom-openai-images";

    /// <summary>Named HTTP client used to download an image returned as an upstream URL.</summary>
    public const string OpenAiDownloadHttpClient = "picloom-openai-image-download";

    /// <summary>Named HTTP client used to download user supplied reference URLs.</summary>
    public const string ReferenceHttpClient = "picloom-reference-fetch";

    public static IServiceCollection AddPicLoomInfrastructure(this IServiceCollection services,
        string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);

        var paths = new PicLoomPaths(dataDirectory);
        services.AddSingleton(paths);
        services.AddDbContext<PicLoomDbContext>(options => options.UseSqlite(paths.ConnectionString));

        services.AddSingleton<ITaskSignal, TaskSignal>();

        // One shared handler for the process: no redirects, 5s connect timeout.
        services.AddSingleton<IProviderTransport, ProviderTransport>();
        services.AddHttpClient(OpenAiHttpClient, client =>
            {
                client.Timeout = OpenAiCompatibleImageProvider.GenerationTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
                provider.GetRequiredService<IProviderTransport>().Create())
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        // Reference downloads and upstream image downloads use a dedicated transport whose
        // ConnectCallback re-resolves and re-validates every address, so a URL can never reach a
        // private/loopback endpoint even through DNS rebinding. The generation client keeps the
        // permissive transport because a local relay may legitimately live on loopback.
        services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        services.AddSingleton<ReferenceTransport>();
        services.AddHttpClient(ReferenceHttpClient, client =>
            {
                client.Timeout = ReferenceFetcher.FetchTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
                provider.GetRequiredService<ReferenceTransport>().Create())
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        services.AddHttpClient(OpenAiDownloadHttpClient, client =>
            {
                client.Timeout = OpenAiCompatibleImageProvider.DownloadTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
                provider.GetRequiredService<ReferenceTransport>().Create())
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        // Registration order defines the order reported by /health.
        services.AddSingleton<IImageProvider>(provider =>
            new OpenAiCompatibleImageProvider(provider.GetRequiredService<IHttpClientFactory>(),
                provider.GetRequiredService<ILogger<OpenAiCompatibleImageProvider>>()));
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();

        services.AddSingleton<IAssetStore, LocalAssetStore>();
        services.AddSingleton<IReferenceStore, LocalReferenceStore>();
        services.AddSingleton<ReferenceFetcher>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddScoped<ProviderSettingsService>();
        services.AddScoped<IProviderSettingsService>(provider =>
            provider.GetRequiredService<ProviderSettingsService>());
        services.AddScoped<IProviderCredentials>(provider =>
            provider.GetRequiredService<ProviderSettingsService>());
        services.AddScoped<IPicLoomService, PicLoomService>();
        services.AddHostedService<GenerationWorker>();

        return services;
    }

    public static async Task InitializeAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var paths = scope.ServiceProvider.GetRequiredService<PicLoomPaths>();
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.AssetsDirectory);
        Directory.CreateDirectory(paths.ReferencesDirectory);
        Directory.CreateDirectory(paths.SecretsDirectory);

        var db = scope.ServiceProvider.GetRequiredService<PicLoomDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            var mode = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("sqlite_wal_unavailable");
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
