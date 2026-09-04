namespace BulkEmailSender.Api.Domain.Email;

public sealed class EmailAttachment
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}