using PicLoom.Core;

namespace PicLoom.Infrastructure.Assets;

/// <summary>
/// Shared file-system primitives for the local asset and reference stores. Both stores write
/// atomically, derive the extension from the magic-byte MIME type and resolve relative paths under
/// a fixed root, so the logic lives here instead of being duplicated.
/// </summary>
internal static class StorageFiles
{
    /// <summary>Maps a magic-byte derived MIME type to the file extension used on disk.</summary>
    public static string ExtensionFor(string mimeType) => mimeType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".png"
    };

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="destination"/> through a temporary file in
    /// <paramref name="tempRoot"/>, so a crash never leaves a partially written final file.
    /// </summary>
    public static async Task WriteAtomicAsync(string tempRoot, string destination, byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tempRoot);
        var temp = Path.Combine(tempRoot, $".pending-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Resolves a stored relative path under <paramref name="root"/>, rejecting escapes.</summary>
    public static string ResolveUnder(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains('\0'))
            throw new DomainException("invalid_asset_path");
        if (Path.IsPathRooted(relativePath))
            throw new DomainException("invalid_asset_path");

        var segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Contains(':')))
            throw new DomainException("invalid_asset_path");

        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(new[] { fullRoot }.Concat(segments).ToArray()));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, comparison))
            throw new DomainException("invalid_asset_path");
        return full;
    }

    /// <summary>Deletes a file if it exists, ignoring transient IO failures.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
