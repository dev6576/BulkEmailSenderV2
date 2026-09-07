using System.Threading.Channels;

namespace BulkEmailSender.Api.Services.Sending;

public sealed class InMemorySendQueue : ISendQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(
            operationId,
            cancellationToken);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(
            cancellationToken);
    }
}