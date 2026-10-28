using System.Windows.Forms;

namespace PicLoom.Host.Ui;

internal sealed class UiDispatcher : IDisposable
{
    private readonly Control dispatcher = new();
    private bool disposed;

    public UiDispatcher()
    {
        _ = dispatcher.Handle;
    }

    public void Post(Action action)
    {
        if (disposed || dispatcher.IsDisposed)
        {
            return;
        }

        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Covers ObjectDisposedException; the message loop is already shutting down.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        dispatcher.Dispose();
    }
}
