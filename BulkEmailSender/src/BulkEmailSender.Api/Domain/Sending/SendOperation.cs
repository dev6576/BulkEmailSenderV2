namespace BulkEmailSender.Api.Domain.Sending;

public sealed class SendOperation
{
    public Guid Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public SendOperationStatus Status { get; set; }

    public int TotalRows { get; init; }

    public int SentRows { get; set; }

    public int FailedRows { get; set; }
}