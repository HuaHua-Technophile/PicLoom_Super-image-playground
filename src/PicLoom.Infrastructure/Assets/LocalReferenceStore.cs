using PicLoom.Core;

namespace PicLoom.Infrastructure.Assets;

/// <summary>
/// Stores user supplied reference images under the data directory's <c>references/</c> folder. The
/// thumbnail is a byte-for-byte copy of the original, mirroring how <see cref="LocalAssetStore"/>
/// produces its thumbnails today.
/// </summary>
public sealed class LocalReferenceStore : IReferenceStore
{
    private readonly PicLoomPaths _paths;

    public LocalReferenceStore(PicLoomPaths paths) => _paths = paths;

    /// <inheritdoc />
    /// <remarks>
    /// NOTE: the thumbnail is currently a byte-for-byte copy of the original, so every reference
    /// costs 2x its size on disk (4 x 9 MiB references = ~72 MiB). Accepted for this increment; a
    /// real downscaled thumbnail can replace this without touching the row schema.
    /// </remarks>
    public async Task<ReferenceImage> SaveAsync(byte[] bytes, string mimeType, int width, int height,
        string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) throw new DomainException("reference_type_invalid");
        if (width <= 0 || height <= 0) throw new DomainException("reference_type_invalid");

        var id = Guid.NewGuid();
        var directoryName = id.ToString("N");
        var directory = Path.Combine(_paths.ReferencesDirectory, directoryName);
        Directory.CreateDirectory(directory);

        var extension = StorageFiles.ExtensionFor(mimeType);
        var imageName = "image" + extension;
        var thumbnailName = "thumbnail" + extension;
        var imagePath = Path.Combine(directory, imageName);
        var thumbnailPath = Path.Combine(directory, thumbnailName);

        try
        {
            await StorageFiles.WriteAtomicAsync(_paths.ReferencesDirectory, imagePath, bytes, cancellationToken)
                .ConfigureAwait(false);
            await StorageFiles.WriteAtomicAsync(_paths.ReferencesDirectory, thumbnailPath, bytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            StorageFiles.TryDelete(imagePath);
            StorageFiles.TryDelete(thumbnailPath);
            throw;
        }

        return new ReferenceImage(id, $"{directoryName}/{imageName}", $"{directoryName}/{thumbnailName}",
            mimeType, width, height, source);
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(ReferenceImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var path = StorageFiles.ResolveUnder(_paths.ReferencesDirectory, image.RelativePath);
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Stream OpenRead(string relativePath)
    {
        var fullPath = StorageFiles.ResolveUnder(_paths.ReferencesDirectory, relativePath);
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <inheritdoc />
    public void Delete(ReferenceImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        StorageFiles.TryDelete(StorageFiles.ResolveUnder(_paths.ReferencesDirectory, image.RelativePath));
        StorageFiles.TryDelete(StorageFiles.ResolveUnder(_paths.ReferencesDirectory, image.ThumbnailPath));
    }
}
