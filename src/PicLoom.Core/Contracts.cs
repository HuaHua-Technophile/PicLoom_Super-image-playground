namespace PicLoom.Core;

public sealed record TemplateInput(string Name, string Body, string[] Tags);
public sealed record GroupInput(string Name);

/// <summary>
/// Task creation body. Every member is optional so both the legacy catalog path
/// (<see cref="TemplateId"/> + <see cref="ResolutionId"/> + <see cref="GroupId"/>) and the
/// inline path (<see cref="Prompt"/> + <see cref="Resolution"/>) are expressible.
/// <see cref="ReferenceIds"/> and <see cref="Options"/> are additive: omitting them keeps the
/// text-to-image behaviour byte for byte.
/// </summary>
public sealed record CreateTaskInput(Guid? TemplateId = null, Guid? ResolutionId = null,
    Guid? GroupId = null, string? Provider = null, string? Model = null, string? Prompt = null,
    ResolutionTarget? Resolution = null, Guid[]? ReferenceIds = null,
    GenerationOptionsInput? Options = null);

/// <summary>One reference image handed to a provider adapter.</summary>
/// <param name="Bytes">Raw file bytes as stored locally.</param>
/// <param name="MimeType">Magic-byte derived MIME type.</param>
/// <param name="FileName">Multipart file name, e.g. <c>input-1.png</c>.</param>
public sealed record ProviderInputImage(byte[] Bytes, string MimeType, string FileName);

public sealed record ProviderRequest(Guid AttemptId, string IdempotencyKey, string Model,
    PromptSnapshot Prompt, ResolutionSnapshot Resolution, ProviderConnection? Connection = null,
    IReadOnlyList<ProviderInputImage>? InputImages = null, GenerationOptions? Options = null,
    Guid? TaskId = null)
{
    /// <summary>Options with the documented defaults applied; never null.</summary>
    public GenerationOptions EffectiveOptions => Options ?? GenerationOptions.Default;

    /// <summary>Input images in ordinal order; empty for text-to-image.</summary>
    public IReadOnlyList<ProviderInputImage> EffectiveInputImages => InputImages ?? [];
}

public sealed record GeneratedImage(byte[] Bytes, string MimeType, int Width, int Height);
public sealed class AmbiguousGenerationException() : Exception("unknown_result");

public interface IImageProvider
{
    string Id { get; }

    /// <summary>True when the adapter needs a stored BYOK credential before it can be dispatched.</summary>
    bool RequiresConnection { get; }

    /// <summary>
    /// Produces one or more images for one dispatch. The returned list is never empty: an upstream
    /// response without images is an <c>InvalidResponse</c> failure.
    /// </summary>
    Task<IReadOnlyList<GeneratedImage>> GenerateAsync(ProviderRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Resolves provider adapters by identifier.</summary>
public interface IProviderRegistry
{
    /// <summary>Registered provider identifiers in registration order.</summary>
    IReadOnlyList<string> Ids { get; }

    /// <summary>Finds an adapter by identifier, or null when the identifier is unknown.</summary>
    IImageProvider? Find(string id);
}

/// <summary>Supplies the in-memory credentials for a provider, or null when it is not configured.</summary>
public interface IProviderCredentials
{
    Task<ProviderConnection?> ResolveAsync(string providerId, CancellationToken cancellationToken);
}

/// <summary>BYOK settings use cases, kept separate from <see cref="IPicLoomService"/>.</summary>
public interface IProviderSettingsService
{
    Task<ProviderSettingsView> GetAsync(CancellationToken cancellationToken);
    Task<ProviderSettingsView> UpsertAsync(ProviderSettingsInput input, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
    Task<ProviderTestResult> TestAsync(ProviderSettingsInput? input, CancellationToken cancellationToken);
}

public interface ISecretStore
{
    Task SaveAsync(string providerOrigin, string secret, CancellationToken cancellationToken = default);
    Task<string?> ReadAsync(string providerOrigin, CancellationToken cancellationToken = default);
    Task DeleteAsync(string providerOrigin, CancellationToken cancellationToken = default);
}

public interface IAssetStore
{
    /// <summary>
    /// Saves every produced image of one attempt. The operation is all-or-nothing: if any file
    /// cannot be written, the files written so far are removed and the exception propagates.
    /// </summary>
    Task<IReadOnlyList<Asset>> SaveAsync(Guid taskId, Guid attemptId,
        IReadOnlyList<GeneratedImage> images, CancellationToken cancellationToken);

    Stream OpenRead(string relativePath);
}

/// <summary>File storage for user supplied reference images.</summary>
public interface IReferenceStore
{
    /// <summary>Writes the image and its thumbnail and returns the unbound row to persist.</summary>
    Task<ReferenceImage> SaveAsync(byte[] bytes, string mimeType, int width, int height, string source,
        CancellationToken cancellationToken);

    /// <summary>Reads the stored bytes of an existing reference image.</summary>
    Task<byte[]> ReadAsync(ReferenceImage image, CancellationToken cancellationToken);

    /// <summary>Opens the stored image for streaming responses.</summary>
    Stream OpenRead(string relativePath);

    /// <summary>Best-effort removal of the stored files; a missing file is not an error.</summary>
    void Delete(ReferenceImage image);
}

public interface ITaskSignal
{
    void Wake();
    Task WaitAsync(CancellationToken cancellationToken);
}

public interface IPicLoomService
{
    Task<IReadOnlyList<PromptTemplate>> GetTemplatesAsync(CancellationToken ct);
    Task<PromptTemplate> GetTemplateAsync(Guid id, CancellationToken ct);
    Task<PromptTemplate> CreateTemplateAsync(TemplateInput input, CancellationToken ct);
    Task<PromptTemplate> UpdateTemplateAsync(Guid id, TemplateInput input, CancellationToken ct);
    Task DeleteTemplateAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ResolutionProfile>> GetResolutionsAsync(CancellationToken ct);
    Task<ResolutionProfile> GetResolutionAsync(Guid id, CancellationToken ct);
    Task<ResolutionProfile> CreateResolutionAsync(ResolutionInput input, CancellationToken ct);
    Task<ResolutionProfile> UpdateResolutionAsync(Guid id, ResolutionInput input, CancellationToken ct);
    Task DeleteResolutionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<TaskGroup>> GetGroupsAsync(CancellationToken ct);
    Task<TaskGroup> GetGroupAsync(Guid id, CancellationToken ct);
    Task<TaskGroup> CreateGroupAsync(GroupInput input, CancellationToken ct);
    Task<TaskGroup> UpdateGroupAsync(Guid id, GroupInput input, CancellationToken ct);
    Task DeleteGroupAsync(Guid id, CancellationToken ct);
    Task<GenerationTask> CreateTaskAsync(CreateTaskInput input, CancellationToken ct);
    Task<IReadOnlyList<GenerationTask>> GetTasksAsync(Guid? groupId, CancellationToken ct);
    Task<GenerationTask> GetTaskAsync(Guid id, CancellationToken ct);
    Task<Asset> GetAssetAsync(Guid id, CancellationToken ct);

    /// <summary>Validates and stores an uploaded reference image (≤10 MiB, png/jpeg/webp).</summary>
    Task<ReferenceImageView> CreateReferenceAsync(byte[] bytes, CancellationToken ct);

    /// <summary>Fetches an https reference image and stores it.</summary>
    Task<ReferenceImageView> CreateReferenceFromUrlAsync(string? url, CancellationToken ct);

    /// <summary>Loads a stored reference image row, or throws <c>reference_not_found</c>.</summary>
    Task<ReferenceImage> GetReferenceAsync(Guid id, CancellationToken ct);

    /// <summary>Deletes an unbound reference image; bound images are refused.</summary>
    Task DeleteReferenceAsync(Guid id, CancellationToken ct);
}
