using System.Net.Mail;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Validation;

namespace BulkEmailSender.Api.Services.Validation;

public sealed class RecipientValidator
{
    public ValidationResult Validate(Recipient recipient)
    {
        var result = new ValidationResult();

        if (!recipient.Values.TryGetValue("Email", out var email) ||
            string.IsNullOrWhiteSpace(email))
        {
            result.Add(
                "MissingEmail",
                "Email address is required.",
                "Email");

            return result;
        }

        if (!IsValidEmail(email))
        {
            result.Add(
                "InvalidEmail",
                "Email address is not valid.",
                "Email");
        }

        return result;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);

            return string.Equals(
                address.Address,
                email,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}