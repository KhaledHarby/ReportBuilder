using System.Threading.Channels;

namespace ReportBuilder.Infrastructure.Cache;

public sealed class DatasetCacheRefreshQueue
{
    private readonly Channel<(Guid DatasetId, Guid? UserId)> _channel =
        Channel.CreateUnbounded<(Guid DatasetId, Guid? UserId)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(Guid datasetId, Guid? userId)
        => _channel.Writer.TryWrite((datasetId, userId));

    public bool TryRead(out (Guid DatasetId, Guid? UserId) item)
        => _channel.Reader.TryRead(out item);

    public Task<bool> WaitToReadAsync(CancellationToken cancellationToken)
        => _channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
}
