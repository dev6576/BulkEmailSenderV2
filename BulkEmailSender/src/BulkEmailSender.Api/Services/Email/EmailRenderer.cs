using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Validation;
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

    public EmailRenderResult Render(
        Recipient recipient,
        EmailDefinition email)
    {
        var validation =
            _validator.Validate(
                recipient,
                email);

        if (!validation.IsValid)
        {
            return EmailRenderResult.Failed(
                recipient.RowId,
                validation.Errors);
        }

        var subject =
            _templateRenderer.RenderText(
                email.Subject,
                recipient.Values);

        var htmlBody =
            _templateRenderer.Render(
                email.Body,
                recipient.Values);

        var renderedEmail = new RenderedEmail
        {
            Subject = subject,
            HtmlBody = htmlBody,
            Attachments = email.Attachments
        };

        return EmailRenderResult.Success(
            recipient.RowId,
            renderedEmail);
    }
}
