using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Services.Validation;

namespace BulkEmailSender.Api.Tests.Services.Validation;

public class RecipientValidatorTests
{
    private readonly RecipientValidator _validator = new();

    [Fact]
    public void ValidEmail_IsValid()
    {
        var recipient = CreateRecipient(
            "alice@example.com");

        var result = _validator.Validate(recipient);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void MissingEmail_IsInvalid()
    {
        var recipient = new Recipient(
            1,
            new Dictionary<string, string>
            {
                ["Name"] = "Alice"
            });

        var result = _validator.Validate(recipient);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == "MissingEmail");
    }

    [Fact]
    public void InvalidEmail_IsInvalid()
    {
        var recipient = CreateRecipient(
            "not-an-email");

        var result = _validator.Validate(recipient);

        Assert.False(result.IsValid);

        Assert.Contains(
            result.Errors,
            error => error.Code == "InvalidEmail");
    }

    [Fact]
    public void EmailColumnLookup_IsCaseInsensitive()
    {
        var recipient = new Recipient(
            1,
            new Dictionary<string, string>
            {
                ["EMAIL"] = "alice@example.com"
            });

        var result = _validator.Validate(recipient);

        Assert.True(result.IsValid);
    }

    private static Recipient CreateRecipient(string email)
    {
        return new Recipient(
            1,
            new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Name"] = "Alice"
            });
    }
}