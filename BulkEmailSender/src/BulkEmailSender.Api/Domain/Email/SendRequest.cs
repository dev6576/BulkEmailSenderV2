namespace BulkEmailSender.Api.Domain.Email;

public sealed class SendRequest
{
    public required IReadOnlyList<Recipient> Recipients { get; init; }

    public required EmailDefinition Email { get; init; }
}