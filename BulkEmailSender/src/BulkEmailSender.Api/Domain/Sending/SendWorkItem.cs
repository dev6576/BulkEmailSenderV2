namespace BulkEmailSender.Api.Domain.Sending;

public sealed class SendWorkItem
{
    public long Id { get; init; }

    public Guid OperationId { get; init; }

    public long RowId { get; init; }

    public required Dictionary<string, string> Values { get; init; }

    public SendWorkItemStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ProviderMessageId { get; set; }
}