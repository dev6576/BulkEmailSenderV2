namespace BulkEmailSender.Api.Domain.Email;

public sealed class EmailSendResult
{
    public required bool IsSuccess { get; init; }

    public string? ProviderMessageId { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    // The orchestrator retries only explicitly transient failures; invalid
    // input and permanent provider rejections should not be repeated.
    public bool IsTransientFailure { get; init; }

    public static EmailSendResult Success(
        string? providerMessageId = null)
    {
        return new EmailSendResult
        {
            IsSuccess = true,
            ProviderMessageId = providerMessageId
        };
    }

    public static EmailSendResult Failure(
        string errorCode,
        string errorMessage,
        bool isTransientFailure = false)
    {
        return new EmailSendResult
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            IsTransientFailure = isTransientFailure
        };
    }
}
