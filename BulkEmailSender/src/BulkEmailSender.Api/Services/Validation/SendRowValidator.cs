using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Validation;
using BulkEmailSender.Api.Services.Template;
using System.Text.RegularExpressions;

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

        if (string.IsNullOrWhiteSpace(email.Subject))
            errors.Add(new ValidationError("SubjectRequired", "Subject is required.", "Subject"));
        // HTML can contain tags but no visible text (for example <p><br></p>);
        // strip markup and decode entities before deciding whether it is empty.
        if (string.IsNullOrWhiteSpace(email.Body) || IsHtmlBodyEmpty(email.Body))
            errors.Add(new ValidationError("BodyRequired", "HTML email body cannot be empty.", "Body"));

        AddMissingTemplateFields(
            errors,
            "Subject",
            email.Subject,
            recipient);

        ValidateBraces(errors, "Subject", email.Subject);
        ValidateBraces(errors, "Body", email.Body);

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

    private static bool IsHtmlBodyEmpty(string body)
    {
        var text = Regex.Replace(body, "<[^>]*>", " ");
        return System.Net.WebUtility.HtmlDecode(text).Replace("\u00a0", " ", StringComparison.Ordinal).Trim().Length == 0;
    }

    private static void ValidateBraces(List<ValidationError> errors, string name, string template)
    {
        // Remove valid {FieldName} tokens first; any remaining brace is likely
        // a malformed merge field that would otherwise leak into the email.
        var unmatched = Regex.Replace(template, @"\{[^{}]+\}", string.Empty);
        if (template.Contains("{{", StringComparison.Ordinal) || template.Contains("}}", StringComparison.Ordinal) ||
            unmatched.Contains('{') || unmatched.Contains('}'))
        {
            errors.Add(new ValidationError("InvalidTemplateBraces", name + " contains invalid template braces. Use {ColumnName} for recipient fields.", name));
        }
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
