using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PicLoom.Host.Infrastructure;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly string mutexName;
    private readonly string pipeName;
    private Mutex? mutex;
    private bool ownsMutex;

    public SingleInstanceCoordinator()
    {
        var userKey = GetCurrentUserKey();
        mutexName = $@"Local\PicLoom.Host.{userKey}";
        pipeName = $"PicLoom.Host.{userKey}";
    }

    public string PipeName => pipeName;

    public bool TryAcquire()
    {
        mutex = new Mutex(initiallyOwned: false, mutexName);
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        return ownsMutex;
    }

    public bool TrySignalOpen(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                return false;
            }

            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                client.Connect((int)Math.Min(remaining, int.MaxValue));
                using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                writer.WriteLine("open");
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (ownsMutex)
        {
            ownsMutex = false;
            try
            {
                mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex is released on the owning STA thread in normal flow.
            }
        }

        mutex?.Dispose();
        mutex = null;
    }

    private static string GetCurrentUserKey()
    {
        string identity;
        try
        {
            using var current = WindowsIdentity.GetCurrent();
            identity = current.User?.Value
                ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        }
        catch
        {
            identity = $"{Environment.UserDomainName}\\{Environment.UserName}";
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(digest.AsSpan(0, 16));
    }
}
