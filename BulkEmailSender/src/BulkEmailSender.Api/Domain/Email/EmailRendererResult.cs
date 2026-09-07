using BulkEmailSender.Api.Domain.Validation;

namespace BulkEmailSender.Api.Domain.Email;

public sealed class EmailRenderResult
{
    public required long RowId { get; init; }

    public required bool IsValid { get; init; }

    public RenderedEmail? Email { get; init; }

    public IReadOnlyList<ValidationError> Errors { get; init; }
        = [];

    public static EmailRenderResult Success(
        long rowId,
        RenderedEmail email)
    {
        return new EmailRenderResult
        {
            RowId = rowId,
            IsValid = true,
            Email = email
        };
    }

    public static EmailRenderResult Failed(
        long rowId,
        IReadOnlyList<ValidationError> errors)
    {
        return new EmailRenderResult
        {
            RowId = rowId,
            IsValid = false,
            Errors = errors
        };
    }
}