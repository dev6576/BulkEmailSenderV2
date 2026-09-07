using BulkEmailSender.Api.Services.Sending;

namespace BulkEmailSender.Api.Tests.Services.Sending;

public sealed class InMemorySendQueueTests
{
    [Fact]
    public async Task EnqueueAsync_ItemCanBeRead()
    {
        var queue = new InMemorySendQueue();

        var operationId = Guid.NewGuid();

        await queue.EnqueueAsync(operationId);

        using var cts =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(1));

        await using var enumerator =
            queue.ReadAllAsync(cts.Token)
                .GetAsyncEnumerator();

        var hasItem =
            await enumerator.MoveNextAsync();

        Assert.True(hasItem);
        Assert.Equal(
            operationId,
            enumerator.Current);
    }

    [Fact]
    public async Task EnqueueAsync_PreservesOrder()
    {
        var queue = new InMemorySendQueue();

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        await queue.EnqueueAsync(first);
        await queue.EnqueueAsync(second);
        await queue.EnqueueAsync(third);

        using var cts =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(1));

        await using var enumerator =
            queue.ReadAllAsync(cts.Token)
                .GetAsyncEnumerator();

        Assert.True(
            await enumerator.MoveNextAsync());

        Assert.Equal(
            first,
            enumerator.Current);

        Assert.True(
            await enumerator.MoveNextAsync());

        Assert.Equal(
            second,
            enumerator.Current);

        Assert.True(
            await enumerator.MoveNextAsync());

        Assert.Equal(
            third,
            enumerator.Current);
    }

    [Fact]
    public async Task ReadAllAsync_WaitsUntilItemIsEnqueued()
    {
        var queue = new InMemorySendQueue();

        using var cts =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(1));

        await using var enumerator =
            queue.ReadAllAsync(cts.Token)
                .GetAsyncEnumerator();

        var readTask =
            enumerator.MoveNextAsync()
                .AsTask();

        Assert.False(
            readTask.IsCompleted);

        var operationId = Guid.NewGuid();

        await queue.EnqueueAsync(operationId);

        Assert.True(
            await readTask);

        Assert.Equal(
            operationId,
            enumerator.Current);
    }
}