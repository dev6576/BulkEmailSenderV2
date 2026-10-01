namespace BulkEmailSender.Api.Contracts;

public sealed class EmailAttachmentRequest
{
    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    public string? ContentBase64 { get; init; }

    // Kept for compatibility with the original System.Text.Json byte[] contract.
    public byte[]? Content { get; init; }
}
