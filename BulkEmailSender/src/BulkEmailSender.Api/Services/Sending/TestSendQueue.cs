using System.Threading.Channels;
using BulkEmailSender.Api.Services.Sending;

namespace BulkEmailSender.Api.Tests.Services.Sending;

public sealed class TestSendQueue : ISendQueue
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