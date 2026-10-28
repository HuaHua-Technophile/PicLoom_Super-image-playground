using PicLoom.Core;

namespace PicLoom.Infrastructure.Providers;

/// <summary>
/// Immutable index over every registered <see cref="IImageProvider"/>. A duplicated identifier is a
/// composition error, so the constructor throws and the host refuses to start.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<string, IImageProvider> _map;

    public ProviderRegistry(IEnumerable<IImageProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _map = new Dictionary<string, IImageProvider>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var provider in providers)
        {
            if (provider is null) continue;
            if (!_map.TryAdd(provider.Id, provider))
                throw new InvalidOperationException($"provider_duplicate:{provider.Id}");
            ordered.Add(provider.Id);
        }

        Ids = ordered.AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Ids { get; }

    /// <inheritdoc />
    public IImageProvider? Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _map.TryGetValue(id, out var provider) ? provider : null;
    }
}
