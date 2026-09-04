namespace BulkEmailSender.Api.Domain.Validation;

public sealed record ValidationError(
    string Code,
    string Message,
    string? Field = null);