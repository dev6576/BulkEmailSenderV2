using System.Net.Mail;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Validation;

namespace BulkEmailSender.Api.Services.Validation;

public sealed class RecipientValidator
{
    public ValidationResult Validate(Recipient recipient)
    {
        var result = new ValidationResult();

        // Spreadsheet headers commonly vary in capitalization, so identify the
        // required Email column without requiring an exact-case header match.
        var email = recipient.Values.FirstOrDefault(pair =>
            pair.Key.Equals("Email", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(email))
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

        var cc = recipient.Values.FirstOrDefault(pair =>
            pair.Key.Equals("email_cc", StringComparison.OrdinalIgnoreCase)).Value;
        if (!string.IsNullOrWhiteSpace(cc) && !IsValidCcList(cc))
            result.Add("InvalidCcEmail", "CC must contain valid email addresses separated by commas.", "email_cc");

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

    private static bool IsValidCcList(string value)
    {
        var addresses = value.Split(',', StringSplitOptions.TrimEntries);
        return addresses.Length > 0 && addresses.All(address =>
            !string.IsNullOrWhiteSpace(address) && IsValidEmail(address));
    }
}
