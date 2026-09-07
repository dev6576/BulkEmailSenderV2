namespace BulkEmailSender.Api.Contracts;

public sealed class SendRowErrorResponse
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}