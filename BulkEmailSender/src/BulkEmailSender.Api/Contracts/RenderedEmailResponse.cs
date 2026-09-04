namespace BulkEmailSender.Api.Contracts;

public sealed class RenderedEmailResponse
{
    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    public IReadOnlyList<EmailAttachmentResponse> Attachments { get; init; }
        = [];
}