using BulkEmailSender.Api.Contracts;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Validation;

namespace BulkEmailSender.Api.Services.Validation;

public sealed class SendRequestValidator(
    EmailSendOptions options,
    SendRowValidator rowValidator,
    EmailAttachmentRequestValidator attachmentValidator)
{
    public IReadOnlyList<ValidationError> Validate(
        BulkEmailSender.Api.Contracts.SendRequest? request,
        out IReadOnlyList<Recipient> recipients,
        out EmailDefinition? email)
    {
        var errors = new List<ValidationError>();
        recipients = [];
        email = null;
        // Reject empty or over-limit batches before mapping them or doing a
        // per-recipient validation pass.
        if (request?.Recipients is null || request.Recipients.Count == 0)
        {
            errors.Add(new("RecipientsRequired", "At least one recipient is required.", "Recipients"));
            return errors;
        }
        if (request.Recipients.Count > options.MaxRecipientsPerOperation)
            errors.Add(new("TooManyRecipients", $"No more than {options.MaxRecipientsPerOperation} recipients are allowed.", "Recipients"));

        if (request.Email is null)
        {
            errors.Add(new("EmailRequired", "Email definition is required.", "Email"));
            return errors;
        }
        if (string.IsNullOrWhiteSpace(request.Email.Subject))
            errors.Add(new("SubjectRequired", "Subject is required.", "Email.Subject"));
        if (string.IsNullOrWhiteSpace(request.Email.Body) || IsHtmlBodyEmpty(request.Email.Body))
            errors.Add(new("BodyRequired", "HTML email body cannot be empty.", "Email.Body"));

        if (!attachmentValidator.TryConvert(request.Email.Attachments, out var attachments, out var attachmentErrors))
            errors.AddRange(attachmentErrors.Select(message => new ValidationError("InvalidAttachment", message, "Email.Attachments")));

        // Build one domain representation used by both validation and storage;
        // null value dictionaries from older clients are treated as empty data.
        var mapped = request.Recipients.Select(r => new Recipient(r.RowId, r.Values ?? [])).ToList();
        foreach (var duplicateId in mapped.GroupBy(r => r.RowId).Where(g => g.Count() > 1).Select(g => g.Key))
            errors.Add(new("DuplicateRowId", $"Recipient row ID {duplicateId} appears more than once.", $"Recipients:{duplicateId}"));

        foreach (var recipient in mapped)
        {
            if (recipient.RowId <= 0)
                errors.Add(new("InvalidRowId", "Recipient row IDs must be positive.", $"Recipients:{recipient.RowId}"));
            if (recipient.Values.Count > 200 || recipient.Values.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 200 || pair.Value is null || pair.Value.Length > 100_000))
                errors.Add(new("InvalidRecipientValues", "Recipient values contain an empty/long field name or an oversized value.", $"Recipients:{recipient.RowId}"));
        }

        // Per-row checks need the final email definition to find missing merge
        // fields, so construct it only when the required content is present.
        if (!string.IsNullOrWhiteSpace(request.Email.Subject) && !string.IsNullOrWhiteSpace(request.Email.Body))
        {
            email = new EmailDefinition
            {
                Subject = request.Email.Subject,
                Body = request.Email.Body,
                Attachments = attachments
            };
            foreach (var recipient in mapped)
                errors.AddRange(rowValidator.Validate(recipient, email).Errors);
        }

        recipients = mapped;
        return errors;
    }

    private static bool IsHtmlBodyEmpty(string body)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]*>", " ");
        text = System.Net.WebUtility.HtmlDecode(text).Replace("\u00a0", " ", StringComparison.Ordinal).Trim();
        return text.Length == 0;
    }
}
