namespace BulkEmailSender.Api.Contracts;

public sealed class SendRecipientRequest
{
    public required long RowId { get; init; }

    public required Dictionary<string, string> Values { get; init; }
}