namespace BulkEmailSender.Api.Domain.Email;

public sealed class RenderedEmail
{
    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    public string? Cc { get; init; }

    public IReadOnlyList<EmailAttachment> Attachments { get; init; }
        = [];
}
