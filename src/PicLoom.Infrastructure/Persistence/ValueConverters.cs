using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PicLoom.Core;

namespace PicLoom.Infrastructure.Persistence;

internal static class UtcDateTimeValues
{
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime FromDatabase(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(value => UtcDateTimeValues.ToUtc(value), value => UtcDateTimeValues.FromDatabase(value))
    {
    }
}

internal sealed class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public UtcNullableDateTimeConverter()
        : base(
            value => value.HasValue ? UtcDateTimeValues.ToUtc(value.Value) : value,
            value => value.HasValue ? UtcDateTimeValues.FromDatabase(value.Value) : value)
    {
    }
}

internal static class JsonValues
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("json_null");

    private static bool TagsEqual(string[]? left, string[]? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static int TagsHash(string[] tags)
    {
        var hash = new HashCode();
        foreach (var tag in tags) hash.Add(tag, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static string[] TagsClone(string[] tags) => tags.ToArray();

    public static readonly ValueComparer<string[]> TagsComparer =
        new((left, right) => TagsEqual(left, right), tags => TagsHash(tags), tags => TagsClone(tags));

    public static readonly ValueComparer<PromptSnapshot> PromptSnapshotComparer =
        new((left, right) => left == right, snapshot => snapshot.GetHashCode(), snapshot => snapshot);

    public static readonly ValueComparer<ResolutionSnapshot> ResolutionSnapshotComparer =
        new((left, right) => left == right, snapshot => snapshot.GetHashCode(), snapshot => snapshot);
}

internal sealed class JsonValueConverter<T> : ValueConverter<T, string> where T : notnull
{
    public JsonValueConverter()
        : base(value => JsonValues.Serialize(value), json => JsonValues.Deserialize<T>(json))
    {
    }
}

/// <summary>
/// JSON converter for a nullable reference-typed property. EF applies value converters to non-null
/// values only, but the converter's generic type must still be nullable to match the property.
/// </summary>
internal sealed class JsonNullableValueConverter<T> : ValueConverter<T?, string?> where T : class
{
    public JsonNullableValueConverter()
        : base(value => value == null ? (string?)null : JsonValues.Serialize(value),
            json => json == null ? (T?)null : JsonValues.Deserialize<T>(json))
    {
    }
}
