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
    var operation =
        new SendOperationEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = nameof(SendOperationStatus.Queued),
            TotalRows = recipients.Count,
            Subject = email.Subject,
            Body = email.Body,
            AttachmentsJson =
                JsonSerializer.Serialize(
                    email.Attachments)
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
                CreatedAt =
                    DateTimeOffset.UtcNow
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
        TotalRows = operation.TotalRows
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

        var events =
            new List<SendEvent>();

        foreach (var workItem in workItems)
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

            events.Add(
                new SendEvent
                {
                    OperationId = operationId,
                    RowId = workItem.RowId,
                    Status = workItem.Status,
                    Errors = errors,
                    ProviderMessageId =
                        workItem.ProviderMessageId
                });
        }

        return events;
    }
}
