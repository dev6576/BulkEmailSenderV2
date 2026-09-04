using BulkEmailSender.Api.Domain.Validation;

namespace BulkEmailSender.Api.Domain.Email;

public sealed class RowValidationResult
{
    public required long RowId { get; init; }

    public required bool IsValid { get; init; }

    public required IReadOnlyList<ValidationError> Errors { get; init; }
}