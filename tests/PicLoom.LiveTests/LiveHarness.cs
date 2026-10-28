using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PicLoom.Api;
using PicLoom.Core;
using PicLoom.Infrastructure;
using PicLoom.Infrastructure.Persistence;
using PicLoom.Infrastructure.Providers;
using PicLoom.Infrastructure.Services;
using Xunit.Sdk;

namespace PicLoom.LiveTests;

/// <summary>
/// One in-process API for one live test. The server side runs on the test host (no port is bound),
/// while the provider adapter keeps its real <see cref="HttpClient"/> stack, so the upstream call
/// leaves this process over the network. Nothing here injects a fake handler.
/// </summary>
/// <remarks>
/// Each instance owns a throw-away data directory: reference images, generated assets, the SQLite
/// database and the DPAPI secret written by <see cref="ApplyCredentialsAsync"/> all stay isolated
/// from the user's real <c>%LOCALAPPDATA%\PicLoom</c>.
/// </remarks>
internal sealed class LiveHarness : IAsyncDisposable
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromMinutes(4);

    private readonly WebApplication _application;
    private readonly string _dataDirectory;

    private LiveHarness(WebApplication application, string dataDirectory, CapturedLogs logs, string model)
    {
        _application = application;
        _dataDirectory = dataDirectory;
        Logs = logs;
        Model = model;
    }

    /// <summary>Every log line the API wrote during this test.</summary>
    public CapturedLogs Logs { get; }

    /// <summary>Model name configured for this run.</summary>
    public string Model { get; }

    /// <summary>Boots the API on the test host and stores the live credentials in its settings row.</summary>
    public static async Task<LiveHarness> StartAsync(LiveCredentials credentials, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var dataDirectory = Path.Combine(Path.GetTempPath(), "PicLoomLiveTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        var logs = new CapturedLogs();
        var application = await ApiApplication.CreateAsync(
            [$"--PicLoom:DataDirectory={dataDirectory}", "--PicLoom:Port=5278"],
            builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Logging.SetMinimumLevel(LogLevel.Information);
                builder.Logging.AddProvider(logs);
            });
        await application.StartAsync(ct).ConfigureAwait(false);

        var harness = new LiveHarness(application, dataDirectory, logs, credentials.Model);
        await harness.ApplyCredentialsAsync(credentials, ct).ConfigureAwait(false);
        return harness;
    }

    /// <summary>Writes the Base URL, key and model through the real settings use case.</summary>
    private async Task ApplyCredentialsAsync(LiveCredentials credentials, CancellationToken ct)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IProviderSettingsService>();
        var view = await settings.UpsertAsync(
            new ProviderSettingsInput(credentials.BaseUrl, credentials.ApiKey, credentials.Model, true), ct)
            .ConfigureAwait(false);
        if (!view.Configured)
            throw new XunitException(
                $"the live credentials could not be configured (baseUrl={credentials.BaseUrl}, key={credentials.MaskedKey})");
    }

    /// <summary>Stores a reference image through the real upload validation path.</summary>
    public async Task<Guid> CreateReferenceAsync(byte[] png, CancellationToken ct)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPicLoomService>();
        var view = await service.CreateReferenceAsync(png, ct).ConfigureAwait(false);
        return view.Id;
    }

    /// <summary>
    /// Creates a task through the real service, wakes the worker and waits for a terminal status.
    /// Exactly one upstream call happens per task: there is no retry and no second dispatch.
    /// </summary>
    public async Task<GenerationTask> RunTaskAsync(string prompt, ResolutionTarget resolution,
        GenerationOptionsInput? options, Guid[]? referenceIds, CancellationToken ct)
    {
        Guid taskId;
        await using (var scope = _application.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPicLoomService>();
            var task = await service.CreateTaskAsync(new CreateTaskInput(
                Provider: ProviderIds.OpenAiImages,
                Model: Model,
                Prompt: prompt,
                Resolution: resolution,
                ReferenceIds: referenceIds ?? [],
                Options: options), ct).ConfigureAwait(false);
            taskId = task.Id;
        }

        // The worker also rechecks every two seconds; waking it keeps the run short.
        _application.Services.GetRequiredService<ITaskSignal>().Wake();
        return await WaitForTerminalAsync(taskId, ct).ConfigureAwait(false);
    }

    private async Task<GenerationTask> WaitForTerminalAsync(Guid taskId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TerminalTimeout;
        var lastStatus = GenerationStatus.Queued;
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = _application.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PicLoomDbContext>();
            var task = await db.Tasks.AsNoTracking()
                .Include(x => x.Assets)
                .Include(x => x.References)
                .FirstOrDefaultAsync(x => x.Id == taskId, ct).ConfigureAwait(false);

            if (task is not null)
            {
                lastStatus = task.Status;
                if (task.Status is GenerationStatus.Succeeded or GenerationStatus.Failed
                    or GenerationStatus.Unknown or GenerationStatus.Cancelled)
                {
                    task.Assets.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));
                    return task;
                }
            }

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        throw new XunitException(
            $"task {taskId} never reached a terminal status within {TerminalTimeout.TotalMinutes:F0} min " +
            $"(last status {lastStatus}).\n{Describe(null)}");
    }

    /// <summary>Reads a stored asset file so the test can prove the bytes are a decodable image.</summary>
    public byte[] ReadAsset(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var store = _application.Services.GetRequiredService<IAssetStore>();
        using var stream = store.OpenRead(asset.RelativePath);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>True when the stored bytes are a PNG/JPEG/WebP with positive dimensions.</summary>
    public bool AssetIsDecodable(Asset asset, out string detail)
    {
        var bytes = ReadAsset(asset);
        if (ImageProbe.TryRead(bytes, out var width, out var height, out var mimeType))
        {
            detail = $"{asset.Id:N} {mimeType} {width}x{height} {bytes.Length}B";
            return true;
        }

        detail = $"{asset.Id:N} undecodable ({bytes.Length}B)";
        return false;
    }

    /// <summary>Task state plus the captured log lines, used in every failure message.</summary>
    public string Describe(GenerationTask? task)
    {
        var head = task is null
            ? "task=<none>"
            : $"task={task.Id} status={task.Status} errorCode={task.ErrorCode ?? "-"} " +
              $"actualCount={(task.ActualCount?.ToString() ?? "-")} assets={task.Assets.Count} " +
              $"options={(task.Options is null ? "-" : $"count={task.Options.Count}")}";
        return head + Environment.NewLine + "--- captured log ---" + Environment.NewLine + Logs.Dump();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _application.StopAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failed stop must never mask the test result.
        }

        await _application.DisposeAsync().ConfigureAwait(false);
        DeleteDataDirectory();
    }

    /// <summary>Best-effort removal of the throw-away data directory (SQLite keeps files briefly).</summary>
    private void DeleteDataDirectory()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(_dataDirectory))
                    Directory.Delete(_dataDirectory, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}

/// <summary>
/// Collects the formatted output of every <see cref="ILogger"/> the API uses, so a failing live test
/// can print exactly what was sent upstream and what came back.
/// </summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>Every captured line, oldest first.</summary>
    public IReadOnlyList<string> Lines => _lines.ToArray();

    /// <summary>True when any captured line contains <paramref name="fragment"/> (ordinal).</summary>
    public bool Contains(string fragment) =>
        _lines.Any(line => line.Contains(fragment, StringComparison.Ordinal));

    /// <summary>The last <paramref name="maxLines"/> lines joined with newlines.</summary>
    public string Dump(int maxLines = 40)
    {
        var lines = _lines.ToArray();
        var skip = Math.Max(0, lines.Length - maxLines);
        return string.Join(Environment.NewLine, lines.Skip(skip));
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = formatter(state, exception);
            sink.Enqueue($"{logLevel,-11} {category}: {text}");
        }
    }
}
