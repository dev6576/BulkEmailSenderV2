using BulkEmailSender.Api.Domain.Email;

namespace BulkEmailSender.Api.Tests.Domain.Email;

public class SendRequestTests
{
    [Fact]
    public void CanCreateSendRequest()
    {
        var recipient = new Recipient(
            1,
            new Dictionary<string, string>
            {
                ["Email"] = "alice@example.com",
                ["Name"] = "Alice",
                ["Company"] = "Acme"
            });

        var email = new EmailDefinition
        {
            Subject = "Welcome to {Company}",
            Body = "Hi {Name}, welcome to {Company}."
        };

        var request = new SendRequest
        {
            Recipients = [recipient],
            Email = email
        };

        Assert.Single(request.Recipients);
        Assert.Equal(
            "alice@example.com",
            request.Recipients[0].Values["email"]);

        Assert.Equal(
            "Welcome to {Company}",
            request.Email.Subject);
    }
}