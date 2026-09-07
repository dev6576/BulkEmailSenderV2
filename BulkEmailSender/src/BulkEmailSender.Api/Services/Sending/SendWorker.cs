using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BulkEmailSender.Api.Services.Sending;

public sealed class SendWorker(
    ISendQueue queue,
    IServiceScopeFactory scopeFactory,
    SendEventBroadcaster broadcaster,
    ILogger<SendWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Send worker started.");

        await foreach (var operationId in
            queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                logger.LogInformation(
                    "Processing send operation {OperationId}.",
                    operationId);

                using var scope =
                    scopeFactory.CreateScope();

                var operationStore =
                    scope.ServiceProvider
                        .GetRequiredService<SendOperationStore>();

                var operation =
                    await operationStore.GetAsync(
                        operationId,
                        stoppingToken);

                if (operation is null)
                {
                    logger.LogWarning(
                        "Send operation {OperationId} was not found.",
                        operationId);

                    continue;
                }

                logger.LogInformation(
                    "Send operation {OperationId} loaded with {TotalRows} rows.",
                    operationId,
                    operation.TotalRows);

                // Actual row processing goes here.
                //
                // Each completed row should publish:
                //
                // broadcaster.Publish(new SendEvent
                // {
                //     OperationId = operationId,
                //     Type = "RowUpdate",
                //     RowId = workItem.RowId,
                //     Status = workItem.Status,
                //     ...
                // });
                //
                // Once ALL rows have reached Sent/Failed,
                // mark the operation completed in the DB,
                // then publish OperationCompleted.

                // TODO:
                // operationStore.MarkCompletedAsync(...)

                // TODO:
                // broadcaster.Publish(new SendEvent
                // {
                //     OperationId = operationId,
                //     Type = "OperationCompleted",
                //     Status = "Completed",
                //     SentRows = ...,
                //     FailedRows = ...,
                //     TotalRows = operation.TotalRows
                // });
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Send worker is stopping.");

                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Unexpected error while processing send operation {OperationId}.",
                    operationId);
            }
        }

        logger.LogInformation(
            "Send worker stopped.");
    }
}