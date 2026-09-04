using BulkEmailSender.Api.Domain.Validation;

namespace BulkEmailSender.Api.Domain.Email;

public sealed class PreviewResult
{
    public required long RowId { get; init; }

    public required bool IsValid { get; init; }

    public RenderedEmail? Email { get; init; }

    public IReadOnlyList<ValidationError> Errors { get; init; }
        = [];
}