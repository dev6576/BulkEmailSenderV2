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

        operation.Status =
            nameof(SendOperationStatus.Running);

        await _db.SaveChangesAsync(
            cancellationToken);
    }

    public async Task MarkWorkItemSendingAsync(
        long workItemId,
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

        workItem.Status =
            nameof(SendWorkItemStatus.Sending);

        workItem.StartedAt =
            DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(
            cancellationToken);
    }

    public async Task MarkWorkItemSentAsync(
        long workItemId,
        string? providerMessageId,
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
        if (workItem.Status ==
            nameof(SendWorkItemStatus.Sent))
        {
            return;
        }

        workItem.Status =
            nameof(SendWorkItemStatus.Sent);

        workItem.ProviderMessageId =
            providerMessageId;

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

    public async Task CompleteOperationIfFinishedAsync(
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

        var completedRows =
            operation.SentRows +
            operation.FailedRows;

        if (completedRows >= operation.TotalRows)
        {
            operation.Status =
                nameof(SendOperationStatus.Completed);

            await _db.SaveChangesAsync(
                cancellationToken);
        }
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
