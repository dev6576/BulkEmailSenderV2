using BulkEmailSender.Api.Domain.Email;

namespace BulkEmailSender.Api.Services.Providers;

public sealed class StubEmailProvider : IEmailProvider
{
    private readonly Func<
        string,
        RenderedEmail,
        CancellationToken,
        Task<EmailSendResult>> _sendBehavior;

    public StubEmailProvider(
        Func<
            string,
            RenderedEmail,
            CancellationToken,
            Task<EmailSendResult>>? sendBehavior = null)
    {
        _sendBehavior =
            sendBehavior ??
            ((_, _, _) =>
                Task.FromResult(
                    EmailSendResult.Success(
                        $"stub-{Guid.NewGuid():N}")));
    }

    public Task<EmailSendResult> SendAsync(
        string recipientEmail,
        RenderedEmail email,
        CancellationToken cancellationToken)
    {
        return _sendBehavior(
            recipientEmail,
            email,
            cancellationToken);
    }
}