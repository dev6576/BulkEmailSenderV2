using BulkEmailSender.Api.Domain.Validation;

namespace BulkEmailSender.Api.Contracts;

public sealed class PreviewResponse
{
    public required long RowId { get; init; }

    public required bool IsValid { get; init; }

    public RenderedEmailResponse? Email { get; init; }

    public IReadOnlyList<ValidationError> Errors { get; init; }
        = [];
}