using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Sending;
using BulkEmailSender.Api.Services.Auth;

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

        using (var recoveryScope = scopeFactory.CreateScope())
        {
            var recoveryStore = recoveryScope.ServiceProvider.GetRequiredService<SendOperationStore>();
            // The in-memory queue disappears on process exit. Re-queue database
            // operations after resetting any rows interrupted in Sending state.
            await recoveryStore.RecoverInterruptedWorkItemsAsync(stoppingToken);
            foreach (var pendingOperationId in await recoveryStore.GetRecoverableOperationIdsAsync(stoppingToken))
                await queue.EnqueueAsync(pendingOperationId, stoppingToken);
        }

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

        EmailDefinition? email;
        try
        {
            email = JsonSerializer.Deserialize<EmailDefinition>(operation.EmailJson);
        }
        catch (JsonException exception)
        {
            // Corrupt persisted email JSON is a poison operation: failing its
            // pending rows prevents it from being retried and crashing startup
            // every time the worker encounters it.
            logger.LogWarning(exception,
                "Stored email definition for operation {OperationId} is invalid. Marking its pending rows failed.",
                operationId);
            await FailPendingRowsAsync(operationStore, operation, cancellationToken);
            return;
        }

        if (email is null)
        {
            logger.LogWarning("Stored email definition for operation {OperationId} is empty. Marking its pending rows failed.", operationId);
            await FailPendingRowsAsync(operationStore, operation, cancellationToken);
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

            // Only call the provider after the atomic database claim succeeds.
            var claimed = await operationStore.MarkWorkItemSendingAsync(
                workItem.Id,
                cancellationToken);

        if (operation is not null)
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>().SetUserId(operation.UserId);
            if (!claimed) continue;

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
                logger.LogError("Unexpected row processing failure for operation {OperationId}, row {RowId}; exception type {ExceptionType}.", operationId, recipient.RowId, exception.GetType().Name);
                result =
                    SendRowResult.Failed(
                        recipient.RowId,
                        GetRecipientEmail(recipient),
                        "UnexpectedError",
                        "An unexpected processing error occurred for this recipient.");
            }

            if (result.Status == SendRowStatus.Sent)
            {
                await operationStore.MarkWorkItemSentAsync(
                    workItem.Id,
                    result.ProviderMessageId,
                    result.RetryCount,
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
                    result.RetryCount,
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
            logger.LogInformation("Send operation {OperationId} completed with {SentRows} sent and {FailedRows} failed.", completedOperation.Id, completedOperation.SentRows, completedOperation.FailedRows);
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

    private async Task FailPendingRowsAsync(
        SendOperationStore operationStore,
        BulkEmailSender.Api.Data.Entities.SendOperationEntity operation,
        CancellationToken cancellationToken)
    {
        foreach (var item in operation.WorkItems.Where(item =>
                     item.Status == nameof(SendWorkItemStatus.Pending) ||
                     item.Status == nameof(SendWorkItemStatus.Sending)))
        {
            await operationStore.MarkWorkItemFailedAsync(
                item.Id,
                "InvalidStoredEmail",
                "The stored email content is invalid. Create a new send operation.",
                item.RetryCount,
                cancellationToken);
            broadcaster.Publish(new SendEvent
            {
                OperationId = operation.Id,
                RowId = item.RowId,
                Status = nameof(SendWorkItemStatus.Failed),
                Errors = ["InvalidStoredEmail", "The stored email content is invalid. Create a new send operation."]
            });
        }

        var completed = await operationStore.CompleteOperationIfFinishedAsync(operation.Id, cancellationToken);
        if (completed is not null)
        {
            broadcaster.Publish(new SendEvent
            {
                OperationId = completed.Id,
                RowId = 0,
                Status = completed.Status,
                TotalRows = completed.TotalRows,
                SentRows = completed.SentRows,
                FailedRows = completed.FailedRows
            });
        }
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
