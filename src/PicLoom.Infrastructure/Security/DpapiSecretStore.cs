using System.Security.Cryptography;
using System.Text;
using PicLoom.Core;

namespace PicLoom.Infrastructure.Security;

public sealed class DpapiSecretStore : ISecretStore
{
    private const string EntropyPrefix = "PicLoom.SecretStore.v1|";
    private readonly PicLoomPaths _paths;

    public DpapiSecretStore(PicLoomPaths paths) => _paths = paths;

    public async Task SaveAsync(string providerOrigin, string secret, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("dpapi_windows_only");

        var origin = NormalizeOrigin(providerOrigin);
        if (string.IsNullOrEmpty(secret))
            throw new DomainException("secret_required");
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_paths.SecretsDirectory);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] ciphertext;
        try
        {
            ciphertext = ProtectedData.Protect(plaintext, CreateEntropy(origin), DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var destination = Path.Combine(_paths.SecretsDirectory, FileName(origin));
        var temp = Path.Combine(_paths.SecretsDirectory, $".pending-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, ciphertext, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public async Task<string?> ReadAsync(string providerOrigin, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("dpapi_windows_only");

        var origin = NormalizeOrigin(providerOrigin);
        var path = Path.Combine(_paths.SecretsDirectory, FileName(origin));
        if (!File.Exists(path)) return null;

        var ciphertext = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] plaintext;
            try
            {
                plaintext = ProtectedData.Unprotect(ciphertext, CreateEntropy(origin),
                    DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException("secret_unreadable");
            }

            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public Task DeleteAsync(string providerOrigin, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("dpapi_windows_only");

        var origin = NormalizeOrigin(providerOrigin);
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(_paths.SecretsDirectory, FileName(origin));
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public static string NormalizeOrigin(string providerOrigin)
    {
        if (string.IsNullOrWhiteSpace(providerOrigin))
            throw new DomainException("invalid_origin");

        if (!Uri.TryCreate(providerOrigin.Trim(), UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath is not ("" or "/"))
            throw new DomainException("invalid_origin");

        // https is required off loopback; plain http is accepted only for loopback relays.
        var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !(isHttp && ProviderUrl.IsLoopback(uri)))
            throw new DomainException("invalid_origin");

        return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant();
    }

    private static string FileName(string origin) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(origin))) + ".bin";

    private static byte[] CreateEntropy(string origin) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(EntropyPrefix + origin));

    private static void TryDelete(string path)
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
