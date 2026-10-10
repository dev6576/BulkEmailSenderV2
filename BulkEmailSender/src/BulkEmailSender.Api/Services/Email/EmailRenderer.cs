using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Validation;
using BulkEmailSender.Api.Services.Template;
using BulkEmailSender.Api.Services.Validation;
using System.Text.RegularExpressions;

namespace BulkEmailSender.Api.Services.Email;

public sealed class EmailRenderer
{
    private static readonly Regex ParagraphTagRegex =
        new(@"<p\b([^>]*)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            ApplyEmailParagraphSpacing(
                _templateRenderer.Render(email.Body, recipient.Values));

        var renderedEmail = new RenderedEmail
        {
            Subject = subject,
            HtmlBody = htmlBody,
            Cc = recipient.Values.FirstOrDefault(pair =>
                pair.Key.Equals("email_cc", StringComparison.OrdinalIgnoreCase)).Value?.Trim(),
            Attachments = email.Attachments
        };

        return EmailRenderResult.Success(
            recipient.RowId,
            renderedEmail);
    }

    private static string ApplyEmailParagraphSpacing(string html)
    {
        return ParagraphTagRegex.Replace(html, match =>
        {
            var attributes = match.Groups[1].Value;
            const string spacingStyles = "margin: 0; line-height: 1.5; mso-line-height-rule: exactly;";
            var styleMatch = Regex.Match(attributes, @"\sstyle\s*=\s*([""'])(.*?)\1", RegexOptions.IgnoreCase);

            if (styleMatch.Success)
            {
                var style = styleMatch.Groups[2].Value.Trim().TrimEnd(';');
                attributes = attributes.Remove(styleMatch.Index, styleMatch.Length)
                    .Insert(styleMatch.Index, $" style=\"{style}; {spacingStyles}\"");
            }
            else
            {
                attributes += $" style=\"{spacingStyles}\"";
            }

            return $"<p{attributes}>";
        });
    }
}
