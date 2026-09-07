using System.Collections.Concurrent;
using System.Threading.Channels;

namespace BulkEmailSender.Api.Services.Sending;

public sealed class SendEventBroadcaster
{
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<Guid, Channel<SendEvent>>> _subscribers = [];

    public (Guid SubscriptionId, ChannelReader<SendEvent> Reader)
        Subscribe(Guid operationId)
    {
        var subscriptionId = Guid.NewGuid();

        var channel =
            Channel.CreateUnbounded<SendEvent>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

        var operationSubscribers =
            _subscribers.GetOrAdd(
                operationId,
                _ => new ConcurrentDictionary<
                    Guid,
                    Channel<SendEvent>>());

        operationSubscribers[subscriptionId] = channel;

        return (subscriptionId, channel.Reader);
    }

public void Publish(SendEvent sendEvent)
    {
        if (!_subscribers.TryGetValue(
                sendEvent.OperationId,
                out var subscribers))
        {
            return;
        }

        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryWrite(sendEvent);

            if (sendEvent.Type == "OperationCompleted")
            {
                channel.Writer.TryComplete();
            }
        }
    }
    
    public void Unsubscribe(
        Guid operationId,
        Guid subscriptionId)
    {
        if (!_subscribers.TryGetValue(
                operationId,
                out var subscribers))
        {
            return;
        }

        if (subscribers.TryRemove(
                subscriptionId,
                out var channel))
        {
            channel.Writer.TryComplete();
        }

        if (subscribers.IsEmpty)
        {
            _subscribers.TryRemove(
                operationId,
                out _);
        }
    }
}