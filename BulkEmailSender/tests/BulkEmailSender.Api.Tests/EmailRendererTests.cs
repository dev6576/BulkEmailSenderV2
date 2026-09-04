using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Services.Email;
using BulkEmailSender.Api.Services.Template;
using BulkEmailSender.Api.Services.Validation;

namespace BulkEmailSender.Api.Tests.Services.Email;

public class EmailRendererTests
{
    private readonly EmailRenderer _renderer;

    public EmailRendererTests()
    {
        _renderer = new EmailRenderer(
            new SendRowValidator(
                new RecipientValidator(),
                new TemplateRenderer()),
            new TemplateRenderer());
    }

    [Fact]
    public void Render_ValidRecipient_ReturnsRenderedEmail()
    {
        var recipient = CreateRecipient();

        var email = new EmailDefinition
        {
            Subject = "Welcome to {Company}",
            Body = "<p>Hi {Name}</p>"
        };

        var result =
            _renderer.Render(
                recipient,
                email);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Email);

        Assert.Equal(
            "Welcome to Acme",
            result.Email.Subject);

        Assert.Equal(
            "<p>Hi Alice</p>",
            result.Email.HtmlBody);
    }

    [Fact]
    public void Render_InvalidRecipient_ReturnsErrors()
    {
        var recipient = new Recipient(
            1,
            new Dictionary<string, string>
            {
                ["Email"] = "alice@example.com",
                ["Name"] = "Alice"
            });

        var email = new EmailDefinition
        {
            Subject = "Welcome to {Company}",
            Body = "<p>Hi {Name}</p>"
        };

        var result =
            _renderer.Render(
                recipient,
                email);

        Assert.False(result.IsValid);
        Assert.Null(result.Email);

        Assert.Contains(
            result.Errors,
            error => error.Code == "MissingTemplateField");
    }

    [Fact]
    public void Render_PreservesHtml()
    {
        var recipient = CreateRecipient();

        var email = new EmailDefinition
        {
            Subject = "Hello",
            Body = """
                   <h1>Welcome {Name}</h1>
                   <p>Your company is <strong>{Company}</strong>.</p>
                   """
        };

        var result =
            _renderer.Render(
                recipient,
                email);

        Assert.True(result.IsValid);

        Assert.Contains(
            "<strong>Acme</strong>",
            result.Email!.HtmlBody);
    }

    private static Recipient CreateRecipient()
    {
        return new Recipient(
            1,
            new Dictionary<string, string>
            {
                ["Email"] = "alice@example.com",
                ["Name"] = "Alice",
                ["Company"] = "Acme"
            });
    }
}