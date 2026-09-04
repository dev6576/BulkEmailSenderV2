namespace BulkEmailSender.Api.Domain.Template;

public sealed class TemplateValidationResult
{
    private readonly List<TemplateValidationError> _errors = [];

    public IReadOnlyList<TemplateValidationError> Errors => _errors;

    public bool IsValid => _errors.Count == 0;

    public void Add(
        string field,
        string message)
    {
        _errors.Add(
            new TemplateValidationError(field, message));
    }
}

public sealed record TemplateValidationError(
    string Field,
    string Message);