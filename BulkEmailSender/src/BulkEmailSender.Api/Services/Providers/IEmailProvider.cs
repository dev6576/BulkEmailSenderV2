using BulkEmailSender.Api.Domain.Email;

namespace BulkEmailSender.Api.Services.Providers;

public interface IEmailProvider
{
    Task<EmailSendResult> SendAsync(
        string recipientEmail,
        RenderedEmail email,
        CancellationToken cancellationToken);
}