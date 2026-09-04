using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Services.Template;
using BulkEmailSender.Api.Services.Validation;

namespace BulkEmailSender.Api.Tests.Services.Validation;

public class SendRowValidatorTests
{
    private readonly SendRowValidator _validator;

    public SendRowValidatorTests()
    {
        _validator = new SendRowValidator(
            new RecipientValidator(),
            new TemplateRenderer());
    }

    [Fact]
    public void ValidRow_IsValid()
    {
        var recipient = CreateRecipient(
            "alice@example.com",
            "Alice",
            "Acme");

        var email = new EmailDefinition
        {
            Subject = "Welcome to {Company}",
            Body = "Hi {Name}"
        };

        var result =
            _validator.Validate(recipient, email);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InvalidEmail_MakesRowInvalid()
    {
        var recipient = CreateRecipient(
            "not-an-email",
            "Alice",
            "Acme");

        var email = new EmailDefinition
        {
            Subject = "Welcome",
            Body = "Hi {Name}"
        };

        var result =
            _validator.Validate(recipient, email);

        Assert.False(result.IsValid);

        Assert.Contains(
            result.Errors,
            error => error.Code == "InvalidEmail");
    }

    [Fact]
    public void MissingSubjectField_MakesRowInvalid()
    {
        var recipient = CreateRecipient(
            "alice@example.com",
            "Alice",
            "Acme");

        var email = new EmailDefinition
        {
            Subject = "Welcome to {Organisation}",
            Body = "Hi {Name}"
        };

        var result =
            _validator.Validate(recipient, email);

        Assert.False(result.IsValid);

        var error = Assert.Single(
            result.Errors,
            error => error.Code == "MissingTemplateField");

        Assert.Equal(
            "Subject:Organisation",
            error.Field);
    }

    [Fact]
    public void MissingBodyField_MakesRowInvalid()
    {
        var recipient = CreateRecipient(
            "alice@example.com",
            "Alice",
            "Acme");

        var email = new EmailDefinition
        {
            Subject = "Welcome",
            Body = "Hi {FirstName}"
        };

        var result =
            _validator.Validate(recipient, email);

        Assert.False(result.IsValid);

        var error = Assert.Single(
            result.Errors,
            error => error.Code == "MissingTemplateField");

        Assert.Equal(
            "Body:FirstName",
            error.Field);
    }

    [Fact]
    public void MultipleErrors_AreReturnedTogether()
    {
        var recipient = CreateRecipient(
            "not-an-email",
            "Alice",
            "Acme");

        var email = new EmailDefinition
        {
            Subject = "Welcome to {Organisation}",
            Body = "Hi {FirstName}"
        };

        var result =
            _validator.Validate(recipient, email);

        Assert.False(result.IsValid);

        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void TemplateFields_AreCaseInsensitive()
    {
        var recipient = CreateRecipient(
            "alice@example.com",
            "Alice",
            "Acme");

        var email = new EmailDefinition
        {
            Subject = "Welcome to {company}",
            Body = "Hi {NAME}"
        };

        var result =
            _validator.Validate(recipient, email);

        Assert.True(result.IsValid);
    }

    private static Recipient CreateRecipient(
        string email,
        string name,
        string company)
    {
        return new Recipient(
            1,
            new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Name"] = name,
                ["Company"] = company
            });
    }
}