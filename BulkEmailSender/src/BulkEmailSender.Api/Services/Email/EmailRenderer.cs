using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Services.Template;
using BulkEmailSender.Api.Services.Validation;

namespace BulkEmailSender.Api.Services.Email;

public sealed class EmailRenderer
{
    private readonly SendRowValidator _validator;
    private readonly TemplateRenderer _templateRenderer;

    public EmailRenderer(
        SendRowValidator validator,
        TemplateRenderer templateRenderer)
    {
        _validator = validator;
        _templateRenderer = templateRenderer;
    }

    public PreviewResult Render(
        Recipient recipient,
        EmailDefinition email)
    {
        var validationResult =
            _validator.Validate(
                recipient,
                email);

        if (!validationResult.IsValid)
        {
            return new PreviewResult
            {
                RowId = recipient.RowId,
                IsValid = false,
                Errors = validationResult.Errors
            };
        }

        var subject =
            _templateRenderer.Render(
                email.Subject,
                recipient.Values);

        var body =
            _templateRenderer.Render(
                email.Body,
                recipient.Values);

        return new PreviewResult
        {
            RowId = recipient.RowId,
            IsValid = true,
            Email = new RenderedEmail
            {
                Subject = subject,
                HtmlBody = body,
                Attachments = email.Attachments
            }
        };
    }
}