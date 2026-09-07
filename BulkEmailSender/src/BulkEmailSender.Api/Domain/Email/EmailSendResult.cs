namespace BulkEmailSender.Api.Domain.Email;

public sealed class EmailSendResult
{
    public required bool IsSuccess { get; init; }

    public string? ProviderMessageId { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

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
        string errorMessage)
    {
        return new EmailSendResult
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}