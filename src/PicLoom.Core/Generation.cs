namespace PicLoom.Core;

public enum GenerationStatus { Queued, Dispatching, Running, Succeeded, Failed, Unknown, Cancelled }

public static class GenerationStateMachine
{
    public static bool CanTransition(GenerationStatus from, GenerationStatus to) => (from, to) switch
    {
        (GenerationStatus.Queued, GenerationStatus.Dispatching or GenerationStatus.Cancelled or GenerationStatus.Failed) => true,
        (GenerationStatus.Dispatching, GenerationStatus.Running or GenerationStatus.Unknown or GenerationStatus.Failed) => true,
        (GenerationStatus.Running, GenerationStatus.Succeeded or GenerationStatus.Unknown or GenerationStatus.Failed) => true,
        _ => false
    };
}

public sealed class GenerationTask
{
    private GenerationTask() { }
    public GenerationTask(Guid groupId, PromptSnapshot prompt, ResolutionSnapshot resolution, string provider,
        string model, GenerationOptions? options = null)
    {
        GroupId = groupId;
        PromptSnapshot = prompt;
        ResolutionSnapshot = resolution;
        Provider = DomainRule.Text(provider, 80);
        Model = DomainRule.Text(model, 120);
        Options = options ?? GenerationOptions.Default;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid GroupId { get; private set; }
    public PromptSnapshot PromptSnapshot { get; private set; } = null!;
    public ResolutionSnapshot ResolutionSnapshot { get; private set; } = null!;
    public string Provider { get; private set; } = "";
    public string Model { get; private set; } = "";

    /// <summary>Generation options captured at submission time; null only on legacy rows.</summary>
    public GenerationOptions? Options { get; private set; }

    /// <summary>Number of images actually returned by the upstream, or null before/without dispatch.</summary>
    public int? ActualCount { get; private set; }

    /// <summary>
    /// How many reference images were claimed at submission time. The worker compares this with the
    /// rows it can load and refuses to silently degrade an image-to-image task into text-to-image.
    /// </summary>
    public int ExpectedReferenceCount { get; private set; }

    public GenerationStatus Status { get; private set; } = GenerationStatus.Queued;
    public string? ErrorCode { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; private set; }
    public List<Asset> Assets { get; private set; } = [];
    public List<ReferenceImage> References { get; private set; } = [];

    public void TransitionTo(GenerationStatus next, string? errorCode = null)
    {
        DomainRule.Require(GenerationStateMachine.CanTransition(Status, next), "invalid_transition");
        Status = next;
        ErrorCode = errorCode;
        UpdatedAt = DateTime.UtcNow;
        if (next is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Unknown or GenerationStatus.Cancelled)
            CompletedAt = UpdatedAt;
    }

    /// <summary>Records how many images the upstream actually produced for this task.</summary>
    public void SetActualCount(int count)
    {
        DomainRule.Require(count >= 0, "actual_count_invalid");
        ActualCount = count;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records how many reference images this task claimed when it was created.</summary>
    public void SetExpectedReferenceCount(int count)
    {
        DomainRule.Require(count >= 0, "reference_count_invalid");
        ExpectedReferenceCount = count;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Binds the supplied reference images to this task, preserving request order.</summary>
    public void BindReferences(IReadOnlyList<ReferenceImage> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        for (var index = 0; index < references.Count; index++)
        {
            var reference = references[index];
            reference.BindTo(Id, index);
            if (!References.Contains(reference)) References.Add(reference);
        }
    }
}

public sealed class JobAttempt
{
    private JobAttempt() { }
    public JobAttempt(Guid taskId)
    {
        TaskId = taskId;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TaskId { get; private set; }
    public Guid? RetryOfAttemptId { get; private set; }
    public string IdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");
    public string? RemoteId { get; private set; }
    public GenerationStatus Status { get; private set; } = GenerationStatus.Dispatching;
    public string? ErrorCode { get; private set; }
    public DateTime DispatchedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; private set; }

    public void TransitionTo(GenerationStatus next, string? errorCode = null)
    {
        DomainRule.Require(GenerationStateMachine.CanTransition(Status, next), "invalid_transition");
        Status = next;
        ErrorCode = errorCode;
        if (next is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Unknown)
            CompletedAt = DateTime.UtcNow;
    }
}

public sealed class Asset
{
    private Asset() { }
    public Asset(Guid taskId, Guid attemptId, string relativePath, string thumbnailPath,
        string mimeType, int width, int height, string hash, int ordinal = 0)
    {
        TaskId = taskId;
        AttemptId = attemptId;
        RelativePath = relativePath;
        ThumbnailPath = thumbnailPath;
        MimeType = mimeType;
        Width = width;
        Height = height;
        Hash = hash;
        Ordinal = ordinal;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TaskId { get; private set; }
    public Guid AttemptId { get; private set; }
    public string RelativePath { get; private set; } = "";
    public string ThumbnailPath { get; private set; } = "";
    public string MimeType { get; private set; } = "";
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Hash { get; private set; } = "";

    /// <summary>Position inside the response batch, starting at zero.</summary>
    public int Ordinal { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
}
