using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PicLoom.Core;
using PicLoom.Infrastructure.Persistence;

namespace PicLoom.Infrastructure.Generation;

public sealed class GenerationWorker : BackgroundService
{
    private const string UnknownCode = "unknown_result";
    private static readonly TimeSpan IdleRecheck = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShutdownPersistTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITaskSignal _signal;
    private readonly ILogger<GenerationWorker> _logger;

    public GenerationWorker(IServiceScopeFactory scopeFactory, ITaskSignal signal, ILogger<GenerationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverInterruptedAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            _logger.LogError("generation_recovery_failed");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                processed = await ProcessNextAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                _logger.LogError("generation_cycle_failed");
            }

            if (processed) continue;

            try
            {
                await WaitForWorkAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PicLoomDbContext>();
        var task = await db.Tasks
            .Where(x => x.Status == GenerationStatus.Queued)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (task is null) return false;

        var registry = scope.ServiceProvider.GetRequiredService<IProviderRegistry>();
        var provider = registry.Find(task.Provider);
        if (provider is null)
        {
            // Unknown provider: rejected locally, before any attempt row and before any request.
            await RejectBeforeDispatchAsync(db, task, "unsupported_provider", ct).ConfigureAwait(false);
            return true;
        }

        ProviderConnection? connection = null;
        if (provider.RequiresConnection)
        {
            var credentials = scope.ServiceProvider.GetRequiredService<IProviderCredentials>();
            connection = await credentials.ResolveAsync(provider.Id, ct).ConfigureAwait(false);
            if (connection is null)
            {
                // Not configured: rejected locally, still without an attempt and without a request.
                await RejectBeforeDispatchAsync(db, task, "provider_not_configured", ct).ConfigureAwait(false);
                return true;
            }
        }

        var attempt = new JobAttempt(task.Id);
        var claimed = false;
        var claimFrom = task.Status;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            task.TransitionTo(GenerationStatus.Dispatching);
            db.Attempts.Add(attempt);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            // An uncertain commit must not allow an automatic second dispatch.
            claimed = true;
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            LogTransition("claim", task.Id, claimFrom, GenerationStatus.Dispatching);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The winner owns dispatch; the losing worker must not POST.
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (claimed) await PersistUnknownAsync(task.Id, attempt.Id, UnknownCode).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            if (claimed) await PersistUnknownAsync(task.Id, attempt.Id, UnknownCode).ConfigureAwait(false);
            return false;
        }

        try
        {
            var runFrom = task.Status;
            task.TransitionTo(GenerationStatus.Running);
            attempt.TransitionTo(GenerationStatus.Running);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            LogTransition("dispatch", task.Id, runFrom, GenerationStatus.Running);

            // Reference images are read in their bound order so the upstream multipart batch matches
            // what the user submitted.
            var references = await db.References.AsNoTracking()
                .Where(x => x.TaskId == task.Id)
                .OrderBy(x => x.Ordinal)
                .ToListAsync(ct).ConfigureAwait(false);

            // Defence in depth: an image-to-image task must never be silently downgraded to
            // text-to-image. If the rows claimable at submission are not all present, the outcome is
            // unknown, not a different request shape.
            if (references.Count != task.ExpectedReferenceCount)
            {
                _logger.LogWarning(
                    "generation_reference_missing task={TaskId} expected={Expected} actual={Actual}",
                    task.Id, task.ExpectedReferenceCount, references.Count);
                await PersistUnknownAsync(task.Id, attempt.Id, "reference_missing").ConfigureAwait(false);
                return true;
            }

            var referenceStore = scope.ServiceProvider.GetRequiredService<IReferenceStore>();
            var inputImages = new List<ProviderInputImage>(references.Count);
            foreach (var reference in references)
            {
                var bytes = await referenceStore.ReadAsync(reference, ct).ConfigureAwait(false);
                inputImages.Add(new ProviderInputImage(bytes, reference.MimeType,
                    ReferenceFileNames.For(inputImages.Count, reference.MimeType)));
            }

            var request = new ProviderRequest(attempt.Id, attempt.IdempotencyKey, task.Model,
                task.PromptSnapshot, task.ResolutionSnapshot, connection, inputImages,
                task.Options ?? GenerationOptions.Default, task.Id);
            var images = await provider.GenerateAsync(request, ct).ConfigureAwait(false);
            if (images.Count == 0)
                throw new ProviderFailureException(ProviderErrorKind.InvalidResponse);

            var assetStore = scope.ServiceProvider.GetRequiredService<IAssetStore>();
            var assets = await assetStore.SaveAsync(task.Id, attempt.Id, images, ct).ConfigureAwait(false);
            if (assets.Count != images.Count)
                throw new InvalidOperationException("asset_batch_incomplete");

            db.Assets.AddRange(assets);
            task.SetActualCount(assets.Count);
            var successFrom = task.Status;
            task.TransitionTo(GenerationStatus.Succeeded);
            attempt.TransitionTo(GenerationStatus.Succeeded);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            LogTransition("complete", task.Id, successFrom, GenerationStatus.Succeeded);

            _logger.LogInformation(
                "generation_succeeded task={TaskId} attempt={AttemptId} provider={Provider} count={Count}",
                task.Id, attempt.Id, provider.Id, assets.Count);
            return true;
        }
        catch (ProviderFailureException failure)
        {
            // Dispatch already happened: the outcome stays Unknown, only the code is refined.
            await PersistUnknownAsync(task.Id, attempt.Id, failure.Code).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // Failure after dispatch cannot prove that the upstream request was not accepted.
            await PersistUnknownAsync(task.Id, attempt.Id, UnknownCode).ConfigureAwait(false);
            return true;
        }
    }

    private async Task RejectBeforeDispatchAsync(PicLoomDbContext db, GenerationTask task, string code,
        CancellationToken ct)
    {
        try
        {
            var from = task.Status;
            task.TransitionTo(GenerationStatus.Failed, code);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            LogTransition("reject", task.Id, from, GenerationStatus.Failed, code);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker changed the row first; it owns the outcome.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogError("generation_reject_failed task={TaskId}", task.Id);
        }
    }

    private async Task PersistUnknownAsync(Guid taskId, Guid attemptId, string code)
    {
        try
        {
            using var timeout = new CancellationTokenSource(ShutdownPersistTimeout);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PicLoomDbContext>();

            var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, timeout.Token).ConfigureAwait(false);
            GenerationStatus? taskFrom = null;
            if (task is not null && task.Status is GenerationStatus.Dispatching or GenerationStatus.Running)
            {
                taskFrom = task.Status;
                task.TransitionTo(GenerationStatus.Unknown, code);
            }

            var attempt = await db.Attempts.FirstOrDefaultAsync(x => x.Id == attemptId, timeout.Token).ConfigureAwait(false);
            if (attempt is not null && attempt.Status is GenerationStatus.Dispatching or GenerationStatus.Running)
                attempt.TransitionTo(GenerationStatus.Unknown, code);

            await db.SaveChangesAsync(timeout.Token).ConfigureAwait(false);
            if (taskFrom is { } from)
                LogTransition("unknown", taskId, from, GenerationStatus.Unknown, code);
            else
                _logger.LogWarning("generation_unknown taskId={TaskId} attemptId={AttemptId} errorCode={ErrorCode}",
                    taskId, attemptId, code);
        }
        catch (Exception)
        {
            // Startup recovery will conservatively mark any surviving Dispatching/Running rows.
            _logger.LogError("generation_unknown_persist_failed task={TaskId} attempt={AttemptId}", taskId, attemptId);
        }
    }

    private async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        for (var round = 0; round < 3; round++)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PicLoomDbContext>();

                var tasks = await db.Tasks
                    .Where(x => x.Status == GenerationStatus.Dispatching || x.Status == GenerationStatus.Running)
                    .ToListAsync(ct).ConfigureAwait(false);
                var attempts = await db.Attempts
                    .Where(x => x.Status == GenerationStatus.Dispatching || x.Status == GenerationStatus.Running)
                    .ToListAsync(ct).ConfigureAwait(false);

                foreach (var task in tasks)
                {
                    var from = task.Status;
                    task.TransitionTo(GenerationStatus.Unknown, UnknownCode);
                    LogTransition("recover", task.Id, from, GenerationStatus.Unknown, UnknownCode);
                }
                foreach (var attempt in attempts)
                    attempt.TransitionTo(GenerationStatus.Unknown, UnknownCode);

                if (tasks.Count > 0 || attempts.Count > 0)
                {
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    _logger.LogWarning("generation_recovered tasks={TaskCount} attempts={AttemptCount}",
                        tasks.Count, attempts.Count);
                }

                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A conflicting write landed between load and save; reload and try again.
            }
        }

        _logger.LogError("generation_recovery_conflict");
    }

    /// <summary>
    /// Writes one structured line per persisted task state change so a failure can be traced from
    /// the log alone: which task moved from which state to which state, and with what error code.
    /// </summary>
    private void LogTransition(string phase, Guid taskId, GenerationStatus from, GenerationStatus to,
        string? errorCode = null) =>
        _logger.LogInformation(
            "task.transition phase={Phase} taskId={TaskId} from={From} to={To} errorCode={ErrorCode}",
            phase, taskId, from.ToString(), to.ToString(), errorCode ?? "-");

    private async Task WaitForWorkAsync(CancellationToken ct)    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(IdleRecheck);
        try
        {
            await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Periodic database recheck compensates for a signal that arrived before we waited.
        }
    }
}
