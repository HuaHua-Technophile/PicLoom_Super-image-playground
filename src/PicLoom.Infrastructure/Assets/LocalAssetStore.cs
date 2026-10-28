using System.Security.Cryptography;
using PicLoom.Core;
using PicLoom.Infrastructure.Providers;

namespace PicLoom.Infrastructure.Assets;

// Orphaned final files are retained until a reconcile operation can verify database ownership.
public sealed class LocalAssetStore : IAssetStore
{
    private readonly PicLoomPaths _paths;

    public LocalAssetStore(PicLoomPaths paths) => _paths = paths;

    /// <summary>
    /// Writes every image of one response batch. Files are tracked as they are created so a failure
    /// on image N removes images 1..N-1, leaving no half batch on disk.
    /// </summary>
    public async Task<IReadOnlyList<Asset>> SaveAsync(Guid taskId, Guid attemptId,
        IReadOnlyList<GeneratedImage> images, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0) throw new ArgumentException("image_empty", nameof(images));

        foreach (var image in images)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Bytes is not { Length: > 0 })
                throw new ArgumentException("image_empty", nameof(images));
            if (image.Width <= 0 || image.Height <= 0)
                throw new ArgumentException("image_dimensions_invalid", nameof(images));
            if (string.IsNullOrWhiteSpace(image.MimeType))
                throw new ArgumentException("image_mime_required", nameof(images));
        }

        var directoryName = attemptId.ToString("N");
        var directory = Path.Combine(_paths.AssetsDirectory, directoryName);
        Directory.CreateDirectory(directory);

        var written = new List<string>(images.Count * 2);
        try
        {
            var assets = new List<Asset>(images.Count);
            for (var index = 0; index < images.Count; index++)
            {
                var image = images[index];

                // The MIME type is derived from the image magic bytes, so the stored extension follows it.
                var extension = StorageFiles.ExtensionFor(image.MimeType);
                var imageName = $"image-{index + 1}{extension}";
                var thumbnailName = $"thumbnail-{index + 1}{extension}";
                var imagePath = Path.Combine(directory, imageName);
                var thumbnailPath = Path.Combine(directory, thumbnailName);
                var hash = Convert.ToHexStringLower(SHA256.HashData(image.Bytes));

                written.Add(imagePath);
                await StorageFiles.WriteAtomicAsync(_paths.AssetsDirectory, imagePath, image.Bytes,
                    cancellationToken).ConfigureAwait(false);
                written.Add(thumbnailPath);
                await StorageFiles.WriteAtomicAsync(_paths.AssetsDirectory, thumbnailPath, image.Bytes,
                    cancellationToken).ConfigureAwait(false);

                assets.Add(new Asset(taskId, attemptId, $"{directoryName}/{imageName}",
                    $"{directoryName}/{thumbnailName}", image.MimeType, image.Width, image.Height, hash, index));
            }

            return assets;
        }
        catch
        {
            foreach (var path in written) StorageFiles.TryDelete(path);
            throw;
        }
    }

    /// <summary>Opens a stored asset image stream. The path must stay under the assets directory.</summary>
    public Stream OpenRead(string relativePath)
    {
        var fullPath = StorageFiles.ResolveUnder(_paths.AssetsDirectory, relativePath);
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
