namespace BulkEmailSender.Api.Contracts;

public sealed class SendAcceptedResponse
{
    public required Guid OperationId { get; init; }

    public required string Status { get; init; }

    public required int TotalRows { get; init; }
}