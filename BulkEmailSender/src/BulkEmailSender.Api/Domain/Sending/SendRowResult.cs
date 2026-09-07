using BulkEmailSender.Api.Domain.Validation;

namespace BulkEmailSender.Api.Domain.Sending;

public sealed class SendRowResult
{
    public required long RowId { get; init; }

    public required string RecipientEmail { get; init; }

    public required SendRowStatus Status { get; init; }

    public string? ProviderMessageId { get; init; }

    public IReadOnlyList<ValidationError> ValidationErrors { get; init; }
        = [];

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static SendRowResult Sent(
        long rowId,
        string recipientEmail,
        string? providerMessageId)
    {
        return new SendRowResult
        {
            RowId = rowId,
            RecipientEmail = recipientEmail,
            Status = SendRowStatus.Sent,
            ProviderMessageId = providerMessageId
        };
    }

    public static SendRowResult FailedValidation(
        long rowId,
        string recipientEmail,
        IReadOnlyList<ValidationError> errors)
    {
        return new SendRowResult
        {
            RowId = rowId,
            RecipientEmail = recipientEmail,
            Status = SendRowStatus.Failed,
            ValidationErrors = errors
        };
    }

    public static SendRowResult Failed(
        long rowId,
        string recipientEmail,
        string errorCode,
        string errorMessage)
    {
        return new SendRowResult
        {
            RowId = rowId,
            RecipientEmail = recipientEmail,
            Status = SendRowStatus.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}