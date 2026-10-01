using System.Text.Json;
using BulkEmailSender.Api.Data;
using BulkEmailSender.Api.Data.Entities;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Sending;
using Microsoft.EntityFrameworkCore;

namespace BulkEmailSender.Api.Services.Sending;

public sealed class SendOperationStore
{
    private readonly ApplicationDbContext _db;

    public SendOperationStore(
        ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<SendOperation> CreateAsync(
        IReadOnlyList<Recipient> recipients,
        EmailDefinition email,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var operation =
            new SendOperationEntity
            {
                Id = Guid.NewGuid(),
                CreatedAt = now,
                Status = nameof(SendOperationStatus.Queued),
                TotalRows = recipients.Count,
                EmailJson = JsonSerializer.Serialize(email),
                // Keep attachment data in its dedicated operation column as
                // well as in EmailJson: the dedicated snapshot makes the
                // queued operation's attachments directly inspectable without
                // deserializing the complete email definition.
                AttachmentsJson = JsonSerializer.Serialize(email.Attachments),
                Body= email.Body,
                Subject= email.Subject
            };

        foreach (var recipient in recipients)
        {
            operation.WorkItems.Add(
                new SendWorkItemEntity
                {
                    RowId = recipient.RowId,
                    ValuesJson =
                        JsonSerializer.Serialize(
                            recipient.Values),
                    Status =
                        nameof(SendWorkItemStatus.Pending),
                    CreatedAt = now
                });
        }

        _db.SendOperations.Add(operation);

        await _db.SaveChangesAsync(
            cancellationToken);

        // This scoped context is reused while the background worker handles
        // the queued operation. Detach the returned entity graph so later reads
        // see worker updates instead of this newly-created, stale snapshot.
        _db.ChangeTracker.Clear();

        return new SendOperation
        {
            Id = operation.Id,
            CreatedAt = operation.CreatedAt,
            Status = SendOperationStatus.Queued,
            TotalRows = operation.TotalRows,
            SentRows = 0,
            FailedRows = 0
        };
    }

    public async Task<SendOperationEntity?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return await _db.SendOperations
            .Include(operation => operation.WorkItems)
            .SingleOrDefaultAsync(
                operation => operation.Id == operationId,
                cancellationToken);
    }

    public async Task MarkRunningAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation =
            await _db.SendOperations
                .SingleOrDefaultAsync(
                    operation => operation.Id == operationId,
                    cancellationToken);

        if (operation is null)
        {
            return;
        }

        if (operation.Status == nameof(SendOperationStatus.Completed)) return;
        operation.Status =
            nameof(SendOperationStatus.Running);
        operation.StartedAt ??= DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(
            cancellationToken);
    }

    public async Task<bool> MarkWorkItemSendingAsync(
        long workItemId,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        // Claim with one conditional UPDATE: if another worker already changed
        // Pending, zero rows are updated and that worker owns this recipient.
        // Capture the timestamp first so EF Core can bind it as a SQL parameter.
        var updated = await _db.SendWorkItems
            .Where(item => item.Id == workItemId && item.Status == nameof(SendWorkItemStatus.Pending))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, nameof(SendWorkItemStatus.Sending))
                .SetProperty(item => item.StartedAt, startedAt), cancellationToken);
        return updated == 1;
    }

    public async Task MarkWorkItemSentAsync(
        long workItemId,
        string? providerMessageId,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var workItem =
            await _db.SendWorkItems
                .SingleOrDefaultAsync(
                    item => item.Id == workItemId,
                    cancellationToken);

        if (workItem is null)
        {
            return;
        }

        // Idempotency guard.
        // A retried operation can revisit completed items; don't increment its
        // parent totals twice or overwrite the original provider result.
        if (workItem.Status ==
            nameof(SendWorkItemStatus.Sent))
        {
            return;
        }

        workItem.Status =
            nameof(SendWorkItemStatus.Sent);

        workItem.ProviderMessageId =
            providerMessageId;
        workItem.RetryCount = retryCount;

        workItem.ErrorCode = null;
        workItem.ErrorMessage = null;
        workItem.CompletedAt =
            DateTimeOffset.UtcNow;

        var operation =
            await _db.SendOperations
                .SingleOrDefaultAsync(
                    operation =>
                        operation.Id ==
                        workItem.OperationId,
                    cancellationToken);

        if (operation is not null)
        {
            operation.SentRows++;
        }

        await _db.SaveChangesAsync(
            cancellationToken);
    }

    public async Task MarkWorkItemFailedAsync(
        long workItemId,
        string errorCode,
        string errorMessage,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var workItem =
            await _db.SendWorkItems
                .SingleOrDefaultAsync(
                    item => item.Id == workItemId,
                    cancellationToken);

        if (workItem is null)
        {
            return;
        }

        // Idempotency guard.
        // Keep terminal failure writes idempotent for the same reason as sent
        // writes: operation counters represent unique rows, not attempts.
        if (workItem.Status ==
            nameof(SendWorkItemStatus.Failed))
        {
            return;
        }

        workItem.Status =
            nameof(SendWorkItemStatus.Failed);

        workItem.ErrorCode =
            errorCode;

        workItem.ErrorMessage =
            errorMessage;
        workItem.RetryCount = retryCount;

        workItem.ProviderMessageId = null;

        workItem.CompletedAt =
            DateTimeOffset.UtcNow;

        var operation =
            await _db.SendOperations
                .SingleOrDefaultAsync(
                    operation =>
                        operation.Id ==
                        workItem.OperationId,
                    cancellationToken);

        if (operation is not null)
        {
            operation.FailedRows++;
        }

        await _db.SaveChangesAsync(
            cancellationToken);
    }

    public async Task<SendOperationEntity?> CompleteOperationIfFinishedAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation =
            await _db.SendOperations
                .SingleOrDefaultAsync(
                    operation => operation.Id == operationId,
                    cancellationToken);

        if (operation is null)
        {
            return null;
        }

        if (operation.Status == nameof(SendOperationStatus.Completed)) return null;

        var completedRows =
            operation.SentRows +
            operation.FailedRows;

        // Mark an operation complete only after every recipient has a terminal
        // result; this also leaves interrupted work eligible for recovery.
        if (completedRows < operation.TotalRows)
        {
            return null;
        }

        operation.Status =
            nameof(SendOperationStatus.Completed);
        operation.CompletedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(
            cancellationToken);

        return operation;
    }

    public async Task<IReadOnlyList<Guid>> GetRecoverableOperationIdsAsync(CancellationToken cancellationToken)
    {
        return await _db.SendOperations
            .Where(operation => operation.Status == nameof(SendOperationStatus.Queued) || operation.Status == nameof(SendOperationStatus.Running))
            .Select(operation => operation.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task RecoverInterruptedWorkItemsAsync(CancellationToken cancellationToken)
    {
        // A process may stop after claiming a row but before recording its
        // outcome. Return those claims to Pending so startup recovery can retry.
        await _db.SendWorkItems
            .Where(item => item.Status == nameof(SendWorkItemStatus.Sending))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, nameof(SendWorkItemStatus.Pending)), cancellationToken);
        await _db.SendOperations
            .Where(operation => operation.Status == nameof(SendOperationStatus.Running))
            .ExecuteUpdateAsync(setters => setters.SetProperty(operation => operation.Status, nameof(SendOperationStatus.Queued)), cancellationToken);
    }
    public async Task<IReadOnlyList<SendEvent>>
        GetCompletedEventsAsync(
            Guid operationId,
            CancellationToken cancellationToken)
    {
        var workItems =
            await _db.SendWorkItems
                .Where(item =>
                    item.OperationId == operationId &&
                    (
                        item.Status ==
                            nameof(SendWorkItemStatus.Sent) ||
                        item.Status ==
                            nameof(SendWorkItemStatus.Failed)
                    ))
                .OrderBy(item => item.Id)
                .ToListAsync(cancellationToken);

        return workItems
            .Select(item =>
                ToSendEvent(
                    operationId,
                    item))
            .ToList();
    }

    private static SendEvent ToSendEvent(
        Guid operationId,
        SendWorkItemEntity workItem)
    {
        var errors =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
            workItem.ErrorCode))
        {
            errors.Add(
                workItem.ErrorCode);
        }

        if (!string.IsNullOrWhiteSpace(
            workItem.ErrorMessage))
        {
            errors.Add(
                workItem.ErrorMessage);
        }

        return new SendEvent
        {
            OperationId = operationId,
            RowId = workItem.RowId,
            Status = workItem.Status,
            Errors = errors,
            ProviderMessageId =
                workItem.ProviderMessageId
        };
    }
}
