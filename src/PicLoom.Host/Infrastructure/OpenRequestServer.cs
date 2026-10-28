using System.IO.Pipes;
using System.Text;

namespace PicLoom.Host.Infrastructure;

internal sealed class OpenRequestServer : IDisposable
{
    private readonly string pipeName;
    private readonly Action onOpen;
    private readonly CancellationTokenSource cancel = new();
    private Task? loop;

    public OpenRequestServer(string pipeName, Action onOpen)
    {
        this.pipeName = pipeName;
        this.onOpen = onOpen;
    }

    public void Start()
    {
        if (loop is not null)
        {
            throw new InvalidOperationException("The open request server is already running.");
        }

        loop = Task.Run(() => RunAsync(cancel.Token));
    }

    public void Stop()
    {
        if (cancel.IsCancellationRequested)
        {
            return;
        }

        cancel.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // The server is best-effort; shutdown continues regardless.
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(
                    server,
                    new UTF8Encoding(false),
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 128,
                    leaveOpen: true);
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var request = await reader.ReadLineAsync(readTimeout.Token).ConfigureAwait(false);
                if (string.Equals(request?.Trim(), "open", StringComparison.OrdinalIgnoreCase))
                {
                    onOpen();
                }
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (IOException)
            {
                // Retry with a fresh server instance.
            }
            catch (UnauthorizedAccessException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        cancel.Dispose();
    }
}
