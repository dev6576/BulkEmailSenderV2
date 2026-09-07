namespace BulkEmailSender.Api.Contracts;

public sealed class SendRequest
{
    public required IReadOnlyList<SendRecipientRequest> Recipients { get; init; }

    public required EmailDefinitionRequest Email { get; init; }
}