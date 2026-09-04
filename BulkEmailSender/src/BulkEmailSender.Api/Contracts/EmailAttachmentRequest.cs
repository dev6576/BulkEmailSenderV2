namespace BulkEmailSender.Api.Contracts;

public sealed class EmailAttachmentRequest
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}