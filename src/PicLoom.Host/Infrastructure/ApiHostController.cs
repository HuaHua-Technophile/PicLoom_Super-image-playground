using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PicLoom.Api;
using PicLoom.Api.Security;

namespace PicLoom.Host.Infrastructure;

internal sealed class ApiHostController : IDisposable
{
    private WebApplication? app;

    public async Task StartAsync(string[] args)
    {
        WebApplication? created = null;
        try
        {
            created = await ApiApplication.CreateAsync(args).ConfigureAwait(false);
            await created.StartAsync().ConfigureAwait(false);
            app = created;
        }
        catch
        {
            if (created is not null)
            {
                await StopAndDisposeAsync(created).ConfigureAwait(false);
            }

            throw;
        }
    }

    public void OpenInBrowser()
    {
        var current = app ?? throw new InvalidOperationException("PicLoom API is not running.");
        var uri = current.Services.GetRequiredService<LocalSessionService>().CreateLaunchUri();
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public bool ConfirmListening(TimeSpan timeout)
    {
        var current = app;
        if (current is null)
        {
            return false;
        }

        int port;
        try
        {
            port = current.Services.GetRequiredService<PicLoomOptions>().Port;
        }
        catch
        {
            return false;
        }

        if (port is <= 0 or > 65535)
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            using var cancel = new CancellationTokenSource(timeout);
            client.ConnectAsync("127.0.0.1", port, cancel.Token).GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        var current = Interlocked.Exchange(ref app, null);
        if (current is null)
        {
            return;
        }

        try
        {
            Task.Run(() => StopAndDisposeAsync(current)).GetAwaiter().GetResult();
        }
        catch
        {
            // Shutdown is best-effort; the process is already leaving.
        }
    }

    private static async Task StopAndDisposeAsync(WebApplication current)
    {
        try
        {
            await current.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Continue to disposal.
        }

        try
        {
            await current.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort disposal.
        }
    }

    public void Dispose() => Stop();
}
