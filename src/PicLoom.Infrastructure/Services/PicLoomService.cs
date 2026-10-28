using Microsoft.EntityFrameworkCore;
using PicLoom.Core;
using PicLoom.Infrastructure.Persistence;
using PicLoom.Infrastructure.Providers;
using PicLoom.Infrastructure.References;

namespace PicLoom.Infrastructure.Services;

public sealed class PicLoomService : IPicLoomService
{
    private readonly PicLoomDbContext _db;
    private readonly ITaskSignal _signal;
    private readonly IProviderRegistry _registry;
    private readonly IProviderCredentials _credentials;
    private readonly IReferenceStore _referenceStore;
    private readonly ReferenceFetcher _referenceFetcher;

    public PicLoomService(PicLoomDbContext db, ITaskSignal signal, IProviderRegistry registry,
        IProviderCredentials credentials, IReferenceStore referenceStore, ReferenceFetcher referenceFetcher)
    {
        _db = db;
        _signal = signal;
        _registry = registry;
        _credentials = credentials;
        _referenceStore = referenceStore;
        _referenceFetcher = referenceFetcher;
    }

    public async Task<IReadOnlyList<PromptTemplate>> GetTemplatesAsync(CancellationToken ct) =>
        await _db.Templates.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<PromptTemplate> GetTemplateAsync(Guid id, CancellationToken ct) =>
        await _db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
        ?? throw NotFound();

    public async Task<PromptTemplate> CreateTemplateAsync(TemplateInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var template = new PromptTemplate(input.Name, input.Body, input.Tags);
        _db.Templates.Add(template);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return template;
    }

    public async Task<PromptTemplate> UpdateTemplateAsync(Guid id, TemplateInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var template = await _db.Templates.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
                       ?? throw NotFound();
        template.Update(input.Name, input.Body, input.Tags);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return template;
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct)
    {
        var template = await _db.Templates.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
                       ?? throw NotFound();
        _db.Templates.Remove(template);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResolutionProfile>> GetResolutionsAsync(CancellationToken ct) =>
        await _db.Resolutions.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<ResolutionProfile> GetResolutionAsync(Guid id, CancellationToken ct) =>
        await _db.Resolutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
        ?? throw NotFound();

    public async Task<ResolutionProfile> CreateResolutionAsync(ResolutionInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var resolution = new ResolutionProfile(input);
        _db.Resolutions.Add(resolution);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return resolution;
    }

    public async Task<ResolutionProfile> UpdateResolutionAsync(Guid id, ResolutionInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var resolution = await _db.Resolutions.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
                         ?? throw NotFound();
        resolution.Update(input);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return resolution;
    }

    public async Task DeleteResolutionAsync(Guid id, CancellationToken ct)
    {
        var resolution = await _db.Resolutions.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
                         ?? throw NotFound();
        _db.Resolutions.Remove(resolution);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The system "ungrouped" bucket always sorts first.</summary>
    public async Task<IReadOnlyList<TaskGroup>> GetGroupsAsync(CancellationToken ct) =>
        await _db.Groups.AsNoTracking()
            .OrderByDescending(x => x.IsSystem).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<TaskGroup> GetGroupAsync(Guid id, CancellationToken ct) =>
        await _db.Groups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
        ?? throw NotFound();

    public async Task<TaskGroup> CreateGroupAsync(GroupInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var group = new TaskGroup(input.Name);
        _db.Groups.Add(group);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return group;
    }

    public async Task<TaskGroup> UpdateGroupAsync(Guid id, GroupInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var group = await _db.Groups.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
                    ?? throw NotFound();
        if (group.IsSystem) throw new DomainException("group_reserved");
        group.Rename(input.Name);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return group;
    }

    public async Task DeleteGroupAsync(Guid id, CancellationToken ct)
    {
        var group = await _db.Groups.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
                    ?? throw NotFound();
        if (group.IsSystem) throw new DomainException("group_reserved");
        var hasTasks = await _db.Tasks.AnyAsync(x => x.GroupId == id, ct).ConfigureAwait(false);
        if (hasTasks) throw new DomainException("group_not_empty");
        _db.Groups.Remove(group);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a task from either the inline path (prompt + resolution target) or the legacy catalog
    /// path (templateId + resolutionId). Every rule is evaluated locally, before any row is written
    /// and before any upstream request can happen.
    /// </summary>
    public async Task<GenerationTask> CreateTaskAsync(CreateTaskInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. provider must be registered.
        var providerId = input.Provider?.Trim() ?? "";
        var provider = providerId.Length == 0 ? null : _registry.Find(providerId);
        if (provider is null) throw new DomainException("unsupported_provider");

        // 2. model must be present. Model names are free text: a relay may expose any alias, so the
        // only local rule is the persisted column width.
        var model = input.Model?.Trim() ?? "";
        if (model.Length == 0 || model.Length > 120) throw new DomainException("unsupported_model");

        // 3. providers that need BYOK must be configured, with a usable Base URL and a readable secret.
        // An empty Base URL is the same user-facing condition as a missing key: nothing may be sent
        // anywhere, so it is rejected here, before the task row exists and before any network call.
        if (provider.RequiresConnection)
        {
            var connection = await _credentials.ResolveAsync(provider.Id, ct).ConfigureAwait(false);
            if (connection is null) throw new DomainException("provider_not_configured");

            // Defence in depth: there is no default Base URL to fall back to, and a stored key must
            // never be carried to a host the user did not configure.
            if (string.IsNullOrWhiteSpace(connection.BaseUrl))
                throw new DomainException("provider_not_configured");
        }

        // 4/5. exactly one prompt source, then its own validation.
        var prompt = input.Prompt;
        var hasPrompt = !string.IsNullOrWhiteSpace(prompt);
        var hasTemplate = input.TemplateId is { } templateId && templateId != Guid.Empty;
        if (hasPrompt && hasTemplate) throw new DomainException("prompt_source_invalid");
        if (!hasPrompt && !hasTemplate) throw new DomainException("prompt_required");

        PromptSnapshot promptSnapshot;
        if (hasPrompt)
        {
            var body = prompt!.Trim();
            if (body.Length == 0) throw new DomainException("prompt_required");
            if (body.Length > PromptSnapshot.MaxInlineLength) throw new DomainException("prompt_too_long");
            promptSnapshot = PromptSnapshot.Inline(body);
        }
        else
        {
            var template = await _db.Templates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == input.TemplateId, ct).ConfigureAwait(false)
                ?? throw NotFound();
            promptSnapshot = template.Snapshot();
        }

        // 6/7/8. exactly one resolution source, then tier or custom validation.
        var hasResolution = input.Resolution is not null;
        var hasResolutionProfile = input.ResolutionId is { } resolutionId && resolutionId != Guid.Empty;
        if (hasResolution && hasResolutionProfile) throw new DomainException("resolution_source_invalid");
        if (!hasResolution && !hasResolutionProfile) throw new DomainException("resolution_required");

        ResolutionSnapshot resolutionSnapshot;
        if (hasResolution)
        {
            resolutionSnapshot = BuildInlineResolution(input.Resolution!);
        }
        else
        {
            var profile = await _db.Resolutions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == input.ResolutionId, ct).ConfigureAwait(false)
                ?? throw NotFound();
            resolutionSnapshot = profile.Snapshot();
        }

        // 9. an empty group means the built-in "ungrouped" bucket; it is not looked up on purpose so
        // a task can still be created if the seeded row was removed by hand.
        Guid groupId;
        if (input.GroupId is { } requested && requested != Guid.Empty)
        {
            var exists = await _db.Groups.AnyAsync(x => x.Id == requested, ct).ConfigureAwait(false);
            if (!exists) throw NotFound();
            groupId = requested;
        }
        else
        {
            groupId = ReservedIds.UngroupedGroup;
        }

        // 10. generation options are normalized before anything is written.
        var options = GenerationOptions.Normalize(input.Options);

        // 11. reference images: deduplicated, 1..4, each must exist and must be unbound.
        var references = await LoadReferencesAsync(input.ReferenceIds, ct).ConfigureAwait(false);

        var task = new GenerationTask(groupId, promptSnapshot, resolutionSnapshot, provider.Id, model, options);
        task.SetExpectedReferenceCount(references.Count);
        _db.Tasks.Add(task);

        // The task row must exist before the FK can point at it, so the claim is a second write
        // inside one transaction. The claim itself is a single conditional UPDATE per image:
        // "... WHERE Id = @id AND TaskId IS NULL". Affected rows != 1 means another request won the
        // race, so the whole transaction (including the freshly inserted task) is rolled back.
        // There is deliberately no read-check-then-write: that is exactly what allowed two tasks to
        // bind the same reference and made the loser silently dispatch text-to-image.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            for (var ordinal = 0; ordinal < references.Count; ordinal++)
            {
                var referenceId = references[ordinal].Id;
                var affected = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "ReferenceImages"
                    SET "TaskId" = {task.Id}, "Ordinal" = {ordinal}
                    WHERE "Id" = {referenceId} AND "TaskId" IS NULL
                    """, ct).ConfigureAwait(false);
                if (affected != 1) throw new DomainException("reference_in_use");
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        // In-memory projection for the response only; the rows themselves were claimed above.
        task.BindReferences(references);
        _signal.Wake();
        return task;
    }

    /// <summary>
    /// Loads and validates the requested reference images, preserving request order. An empty or
    /// absent array means text-to-image.
    /// </summary>
    private async Task<IReadOnlyList<ReferenceImage>> LoadReferencesAsync(Guid[]? requestedIds,
        CancellationToken ct)
    {
        if (requestedIds is null || requestedIds.Length == 0) return [];

        var ids = requestedIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        if (ids.Length > ReferenceRules.MaxPerTask) throw new DomainException("reference_too_many");

        var loaded = await _db.References.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        if (loaded.Count != ids.Length) throw new DomainException("reference_not_found");
        if (loaded.Any(x => x.TaskId is not null)) throw new DomainException("reference_in_use");

        return ids.Select(id => loaded.Single(x => x.Id == id)).ToArray();
    }

    /// <summary>
    /// Builds the immutable inline snapshot. Requests that carry an explicit <c>mode</c> are handed
    /// straight to <see cref="ResolutionSizing.ToSnapshot"/>; the historical "exactly one of tier or
    /// custom" check is kept verbatim for legacy bodies that still rely on field inference.
    /// </summary>
    private static ResolutionSnapshot BuildInlineResolution(ResolutionTarget target)
    {
        if (target.Mode is null)
        {
            var requestedTier = target.ImageSize is not null || target.AspectRatio is not null;
            var requestedCustom = target.Width is not null || target.Height is not null;

            if (requestedTier && requestedCustom) throw new DomainException("resolution_invalid");
            if (!requestedTier && !requestedCustom) throw new DomainException("resolution_invalid");
        }

        return ResolutionSizing.ToSnapshot(target);
    }

    public async Task<IReadOnlyList<GenerationTask>> GetTasksAsync(Guid? groupId, CancellationToken ct)
    {
        IQueryable<GenerationTask> query = _db.Tasks.AsNoTracking()
            .Include(x => x.Assets.OrderBy(asset => asset.Ordinal))
            .Include(x => x.References.OrderBy(reference => reference.Ordinal));
        if (groupId.HasValue) query = query.Where(x => x.GroupId == groupId.Value);
        var tasks = await query
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var task in tasks) OrderNavigations(task);
        return tasks;
    }

    public async Task<GenerationTask> GetTaskAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.Tasks.AsNoTracking()
            .Include(x => x.Assets.OrderBy(asset => asset.Ordinal))
            .Include(x => x.References.OrderBy(reference => reference.Ordinal))
            .FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
            ?? throw NotFound();
        OrderNavigations(task);
        return task;
    }

    /// <summary>
    /// <c>Include</c> does not order a collection navigation; both batches are ordered in memory so
    /// the API always returns assets and reference images in their recorded ordinal order. Ordinals
    /// are unique per batch, which makes the result deterministic.
    /// </summary>
    private static void OrderNavigations(GenerationTask task)
    {
        task.Assets.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));
        task.References.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));
    }

    public async Task<Asset> GetAssetAsync(Guid id, CancellationToken ct) =>
        await _db.Assets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
        ?? throw NotFound();

    public async Task<ReferenceImageView> CreateReferenceAsync(byte[] bytes, CancellationToken ct)
    {
        var (mimeType, width, height) = ValidateReferenceBytes(bytes);
        return await StoreReferenceAsync(bytes, mimeType, width, height, ReferenceSources.Upload, ct)
            .ConfigureAwait(false);
    }

    public async Task<ReferenceImageView> CreateReferenceFromUrlAsync(string? url, CancellationToken ct)
    {
        var uri = ReferenceUrl.Parse(url);
        var bytes = await _referenceFetcher.FetchAsync(uri, ct).ConfigureAwait(false);
        var (mimeType, width, height) = ValidateReferenceBytes(bytes);
        return await StoreReferenceAsync(bytes, mimeType, width, height, ReferenceSources.Url, ct)
            .ConfigureAwait(false);
    }

    public async Task<ReferenceImage> GetReferenceAsync(Guid id, CancellationToken ct) =>
        await _db.References.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
        ?? throw new DomainException("reference_not_found");

    public async Task DeleteReferenceAsync(Guid id, CancellationToken ct)
    {
        var reference = await _db.References.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
                        ?? throw new DomainException("reference_not_found");
        if (reference.TaskId is not null) throw new DomainException("reference_in_use");

        _db.References.Remove(reference);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _referenceStore.Delete(reference);
    }

    private async Task<ReferenceImageView> StoreReferenceAsync(byte[] bytes, string mimeType, int width,
        int height, string source, CancellationToken ct)
    {
        var reference = await _referenceStore.SaveAsync(bytes, mimeType, width, height, source, ct)
            .ConfigureAwait(false);
        _db.References.Add(reference);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ReferenceImageView.From(reference);
    }

    /// <summary>
    /// Enforces the upload limits and derives the MIME type from magic bytes only; the client
    /// supplied Content-Type is never trusted.
    /// </summary>
    private static (string MimeType, int Width, int Height) ValidateReferenceBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) throw new DomainException("reference_type_invalid");
        if (bytes.Length > ReferenceRules.MaxBytes) throw new DomainException("reference_too_large");

        if (!ImageProbe.TryRead(bytes, out var width, out var height, out var mimeType))
            throw new DomainException("reference_type_invalid");
        if (mimeType is not (ImageProbe.PngMime or ImageProbe.JpegMime or ImageProbe.WebpMime))
            throw new DomainException("reference_type_invalid");

        // A magic-byte header alone is not an image: the container must contain pixel data and be
        // terminated (PNG IDAT+IEND, JPEG SOF, WebP RIFF length + VP8 chunk). Everything is checked
        // before the first byte is written, so a rejected upload leaves no file behind.
        ImageIntegrity.Require(bytes, mimeType);

        if (width > ReferenceRules.MaxEdge || height > ReferenceRules.MaxEdge)
            throw new DomainException("reference_type_invalid");

        return (mimeType, width, height);
    }

    private static DomainException NotFound() => new("not_found");
}
