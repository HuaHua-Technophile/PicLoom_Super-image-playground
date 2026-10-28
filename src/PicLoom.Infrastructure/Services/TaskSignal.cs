using System.Threading.Channels;
using PicLoom.Core;

namespace PicLoom.Infrastructure.Services;

public sealed class TaskSignal : ITaskSignal
{
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Wake() => _channel.Writer.TryWrite(0);

    public async Task WaitAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
}
