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

    // Retries after the first attempt are tracked per recipient for diagnostics,
    // whether the row ultimately succeeds or fails.
    public int RetryCount { get; init; }

    public static SendRowResult Sent(
        long rowId,
        string recipientEmail,
        string? providerMessageId,
        int retryCount = 0)
    {
        return new SendRowResult
        {
            RowId = rowId,
            RecipientEmail = recipientEmail,
            Status = SendRowStatus.Sent,
            ProviderMessageId = providerMessageId,
            RetryCount = retryCount
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
        string errorMessage,
        int retryCount = 0)
    {
        return new SendRowResult
        {
            RowId = rowId,
            RecipientEmail = recipientEmail,
            Status = SendRowStatus.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            RetryCount = retryCount
        };
    }
}
