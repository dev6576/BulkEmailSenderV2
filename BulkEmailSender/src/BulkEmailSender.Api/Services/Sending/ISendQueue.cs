namespace BulkEmailSender.Api.Services.Sending;

public interface ISendQueue
{
    ValueTask EnqueueAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<Guid> ReadAllAsync(
        CancellationToken cancellationToken = default);
}