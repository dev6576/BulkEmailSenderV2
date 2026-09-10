using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Sending;

namespace BulkEmailSender.Api.Services.Sending;

public sealed class SendWorker(
    ISendQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SendWorker> logger,
    SendEventBroadcaster broadcaster)
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
                await ProcessOperationAsync(
                    operationId,
                    stoppingToken);
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

    private async Task ProcessOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing send operation {OperationId}.",
            operationId);

        using var scope =
            scopeFactory.CreateScope();

        var operationStore =
            scope.ServiceProvider
                .GetRequiredService<SendOperationStore>();

        var orchestrator =
            scope.ServiceProvider
                .GetRequiredService<SendOrchestrator>();

        var operation =
            await operationStore.GetAsync(
                operationId,
                cancellationToken);

        if (operation is null)
        {
            logger.LogWarning(
                "Send operation {OperationId} was not found.",
                operationId);

            return;
        }

        var email =
            JsonSerializer.Deserialize<EmailDefinition>(
                operation.EmailJson);

        if (email is null)
        {
            logger.LogError(
                "Email definition for operation {OperationId} could not be deserialized.",
                operationId);

            return;
        }

        await operationStore.MarkRunningAsync(
            operationId,
            cancellationToken);

        var pendingWorkItems =
            operation.WorkItems
                .Where(item =>
                    item.Status ==
                    nameof(SendWorkItemStatus.Pending))
                .OrderBy(item => item.Id)
                .ToList();

        logger.LogInformation(
            "Send operation {OperationId} has {PendingRows} pending rows.",
            operationId,
            pendingWorkItems.Count);

        foreach (var workItem in pendingWorkItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var values =
                JsonSerializer.Deserialize<
                    Dictionary<string, string>>(
                    workItem.ValuesJson)
                ?? new Dictionary<string, string>();

            var recipient =
                new Recipient(
                    workItem.RowId,
                    values);

            await operationStore.MarkWorkItemSendingAsync(
                workItem.Id,
                cancellationToken);

            SendRowResult result;

            try
            {
                result =
                    (await orchestrator.SendAsync(
                        [recipient],
                        email,
                        cancellationToken))
                    .Single();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                result =
                    SendRowResult.Failed(
                        recipient.RowId,
                        GetRecipientEmail(recipient),
                        "UnexpectedError",
                        exception.Message);
            }

            if (result.Status == SendRowStatus.Sent)
            {
                await operationStore.MarkWorkItemSentAsync(
                    workItem.Id,
                    result.ProviderMessageId,
                    cancellationToken);
            }
            else
            {
                await operationStore.MarkWorkItemFailedAsync(
                    workItem.Id,
                    result.ErrorCode ??
                    "SendFailed",
                    result.ErrorMessage ??
                    "The email could not be sent.",
                    cancellationToken);
            }

            broadcaster.Publish(
                new SendEvent
                {
                    OperationId = operationId,
                    RowId = result.RowId,
                    Status =
                        result.Status ==
                        SendRowStatus.Sent
                            ? nameof(SendWorkItemStatus.Sent)
                            : nameof(SendWorkItemStatus.Failed),
                    Errors = BuildErrors(result),
                    ProviderMessageId =
                        result.ProviderMessageId
                });
        }

        var completedOperation =await operationStore.CompleteOperationIfFinishedAsync(
            operationId,
            cancellationToken);

        if (completedOperation is not null)
        {
            broadcaster.Publish(
                new SendEvent
                {
                    OperationId = completedOperation.Id,
                    RowId = 0,
                    Status = completedOperation.Status,
                    TotalRows = completedOperation.TotalRows,
                    SentRows = completedOperation.SentRows,
                    FailedRows = completedOperation.FailedRows
                });
        }

        logger.LogInformation(
            "Send operation {OperationId} finished.",
            operationId);
    }

    private static IReadOnlyList<string> BuildErrors(
        SendRowResult result)
    {
        var errors = new List<string>();

        foreach (var error in result.ValidationErrors)
        {
            errors.Add(error.ToString());
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            errors.Add(result.ErrorCode);
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            errors.Add(result.ErrorMessage);
        }

        return errors;
    }

    private static string GetRecipientEmail(
        Recipient recipient)
    {
        return recipient.Values
            .FirstOrDefault(
                pair => pair.Key.Equals(
                    "Email",
                    StringComparison.OrdinalIgnoreCase))
            .Value
            ?? string.Empty;
    }
}
