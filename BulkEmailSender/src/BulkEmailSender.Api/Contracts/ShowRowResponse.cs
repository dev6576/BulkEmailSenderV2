namespace BulkEmailSender.Api.Contracts;

public sealed class SendRowResponse
{
    public required long RowId { get; init; }

    public required string RecipientEmail { get; init; }

    public required string Status { get; init; }

    public string? ProviderMessageId { get; init; }

    public IReadOnlyList<SendRowErrorResponse> Errors { get; init; }
        = [];
}