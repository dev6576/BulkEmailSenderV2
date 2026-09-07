namespace BulkEmailSender.Api.Services.Sending;

public sealed class SendEvent
{
    public Guid OperationId { get; init; }

    public string Type { get; init; } = "RowUpdate";

    public long? RowId { get; init; }

    public string Status { get; init; } = null!;

    public IReadOnlyList<string> Errors { get; init; }
        = [];

    public string? ProviderMessageId { get; init; }

    public int? SentRows { get; init; }

    public int? FailedRows { get; init; }

    public int? TotalRows { get; init; }
}