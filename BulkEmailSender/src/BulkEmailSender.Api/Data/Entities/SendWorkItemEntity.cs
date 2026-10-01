namespace BulkEmailSender.Api.Data.Entities;

public sealed class SendWorkItemEntity
{
    public long Id { get; set; }

    public Guid OperationId { get; set; }

    public long RowId { get; set; }

    public string ValuesJson { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // Attempts are per recipient, so one failing address does not hide the
    // retry history of the other rows in this operation.
    public int RetryCount { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ProviderMessageId { get; set; }

    public SendOperationEntity Operation { get; set; } = null!;
}
