namespace BulkEmailSender.Api.Contracts;

public sealed class EmailAttachmentResponse
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }
}