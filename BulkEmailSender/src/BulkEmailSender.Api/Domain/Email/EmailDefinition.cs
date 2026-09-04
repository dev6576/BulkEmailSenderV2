namespace BulkEmailSender.Api.Domain.Email;

public sealed class EmailDefinition
{
    public required string Subject { get; init; }

    public required string Body { get; init; }

    public IReadOnlyList<EmailAttachment> Attachments { get; init; }
        = [];
}