using System.Text.Json.Serialization;

namespace PicLoom.Core;

public sealed class DomainException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class DomainRule
{
    public static void Require(bool valid, string code = "validation_failed")
    {
        if (!valid) throw new DomainException(code);
    }

    public static string Text(string? value, int max)
    {
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= max);
        return value!.Trim();
    }
}

public sealed class PromptTemplate
{
    private PromptTemplate() { }
    public PromptTemplate(string name, string body, string[] tags) => Update(name, body, tags);
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = "";
    public string Body { get; private set; } = "";
    public string[] Tags { get; private set; } = [];
    public int Revision { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public void Update(string name, string body, string[] tags)
    {
        var validName = DomainRule.Text(name, 120);
        var validBody = DomainRule.Text(body, 20000);
        DomainRule.Require(tags is { Length: <= 20 });
        var validTags = tags.Select(t => DomainRule.Text(t, 40)).Distinct().ToArray();
        Name = validName;
        Body = validBody;
        Tags = validTags;
        Revision++;
        UpdatedAt = DateTime.UtcNow;
    }

    public PromptSnapshot Snapshot() => PromptSnapshot.FromTemplate(this);
}

/// <summary>
/// Well-known identifiers that must never be created by user requests.
/// </summary>
public static class ReservedIds
{
    /// <summary>The built-in system group that receives every ungrouped task.</summary>
    public static readonly Guid UngroupedGroup = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Name persisted for the system group; the UI renders it through i18n.</summary>
    public const string UngroupedGroupName = "未分组";

    /// <summary>Creation/update timestamp of seeded rows, so seeding stays deterministic.</summary>
    public static readonly DateTime SeedTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

public enum ResolutionMode
{
    [JsonStringEnumMemberName("exactPixels")] ExactPixels,
    [JsonStringEnumMemberName("aspectClass")] AspectClass,

    /// <summary>
    /// The model decides its own output size. Appended <em>after</em> the two original members on
    /// purpose: the persisted snapshot stores this enum as a number, so inserting a member anywhere
    /// else would silently re-label every historical row.
    /// </summary>
    [JsonStringEnumMemberName("auto")] Auto
}

public sealed record ResolutionInput(string Name, ResolutionMode Mode, int? Width, int? Height,
    string? AspectRatio, string? ImageSize);

public sealed class ResolutionProfile
{
    private ResolutionProfile() { }
    public ResolutionProfile(ResolutionInput input) => Update(input);
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = "";
    public ResolutionMode Mode { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public string? AspectRatio { get; private set; }
    public string? ImageSize { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public void Update(ResolutionInput input)
    {
        var name = DomainRule.Text(input.Name, 120);
        DomainRule.Require(input.Mode switch
        {
            ResolutionMode.ExactPixels => input.Width is >= 1 and <= 8192 && input.Height is >= 1 and <= 8192
                && input.AspectRatio is null && input.ImageSize is null,
            ResolutionMode.AspectClass => input.Width is null && input.Height is null
                && input.AspectRatio is "1:1" or "16:9" or "9:16" or "4:3" or "3:4"
                && input.ImageSize is "1K" or "2K" or "4K",
            _ => false
        });
        Name = name;
        Mode = input.Mode;
        Width = input.Width;
        Height = input.Height;
        AspectRatio = input.AspectRatio;
        ImageSize = input.ImageSize;
        UpdatedAt = DateTime.UtcNow;
    }

    public ResolutionSnapshot Snapshot() => new(Id, Name, Mode, Width, Height, AspectRatio, ImageSize);
}

public sealed class TaskGroup
{
    private TaskGroup() { }
    public TaskGroup(string name) => Rename(name);
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = "";

    /// <summary>
    /// True for built-in groups (the "ungrouped" bucket). System groups cannot be renamed or
    /// deleted and always sort first.
    /// </summary>
    public bool IsSystem { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public void Rename(string name)
    {
        Name = DomainRule.Text(name, 120);
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>Where the immutable prompt snapshot came from.</summary>
public enum PromptSource
{
    [JsonStringEnumMemberName("template")] Template,
    [JsonStringEnumMemberName("inline")] Inline
}

/// <summary>
/// Immutable prompt captured when the task was submitted. <see cref="Source"/> is the trailing
/// parameter with a default so snapshots written before this field existed stay readable.
/// </summary>
public sealed record PromptSnapshot(Guid TemplateId, int Revision, string Name, string Body,
    PromptSource Source = PromptSource.Template)
{
    /// <summary>Largest accepted inline prompt length.</summary>
    public const int MaxInlineLength = 8000;

    /// <summary>Captures a catalog preset.</summary>
    public static PromptSnapshot FromTemplate(PromptTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new PromptSnapshot(template.Id, template.Revision, template.Name, template.Body,
            PromptSource.Template);
    }

    /// <summary>Captures an ad-hoc prompt typed on the task page.</summary>
    public static PromptSnapshot Inline(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new DomainException("prompt_required");
        var value = body.Trim();
        if (value.Length > MaxInlineLength) throw new DomainException("prompt_too_long");
        return new PromptSnapshot(Guid.Empty, 0, "", value, PromptSource.Inline);
    }
}

public sealed record ResolutionSnapshot(Guid ProfileId, string Name, ResolutionMode Mode, int? Width,
    int? Height, string? AspectRatio, string? ImageSize);
