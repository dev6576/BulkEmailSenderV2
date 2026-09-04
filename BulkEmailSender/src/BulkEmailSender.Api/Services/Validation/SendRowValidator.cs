using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Validation;
using BulkEmailSender.Api.Services.Template;

namespace BulkEmailSender.Api.Services.Validation;

public sealed class SendRowValidator
{
    private readonly RecipientValidator _recipientValidator;
    private readonly TemplateRenderer _templateRenderer;

    public SendRowValidator(
        RecipientValidator recipientValidator,
        TemplateRenderer templateRenderer)
    {
        _recipientValidator = recipientValidator;
        _templateRenderer = templateRenderer;
    }

    public RowValidationResult Validate(
        Recipient recipient,
        EmailDefinition email)
    {
        var errors = new List<ValidationError>();

        var recipientResult =
            _recipientValidator.Validate(recipient);

        errors.AddRange(recipientResult.Errors);

        AddMissingTemplateFields(
            errors,
            "Subject",
            email.Subject,
            recipient);

        AddMissingTemplateFields(
            errors,
            "Body",
            email.Body,
            recipient);

        return new RowValidationResult
        {
            RowId = recipient.RowId,
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private void AddMissingTemplateFields(
        List<ValidationError> errors,
        string templateName,
        string template,
        Recipient recipient)
    {
        var missingFields =
            _templateRenderer.GetMissingFields(
                template,
                recipient.Values);

        foreach (var field in missingFields)
        {
            errors.Add(
                new ValidationError(
                    "MissingTemplateField",
                    $"Template field '{field}' does not exist in the recipient data.",
                    $"{templateName}:{field}"));
        }
    }
}