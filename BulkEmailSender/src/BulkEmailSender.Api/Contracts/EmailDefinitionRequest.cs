namespace BulkEmailSender.Api.Contracts;

public sealed class EmailDefinitionRequest
{
    public required string Subject { get; init; }

    public required string Body { get; init; }

    public IReadOnlyList<EmailAttachmentRequest> Attachments { get; init; }
        = [];
}