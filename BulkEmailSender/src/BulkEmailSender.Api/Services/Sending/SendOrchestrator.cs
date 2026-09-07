using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Sending;
using BulkEmailSender.Api.Services.Email;
using BulkEmailSender.Api.Services.Providers;
using BulkEmailSender.Api.Services.Validation;

namespace BulkEmailSender.Api.Services.Sending;

public sealed class SendOrchestrator
{
    private readonly EmailRenderer _renderer;
    private readonly IEmailProvider _provider;
    public SendOrchestrator(
        EmailRenderer renderer,
        IEmailProvider provider)
    {
        _renderer = renderer;
        _provider = provider;
    }

    public async Task<IReadOnlyList<SendRowResult>> SendAsync(
        IReadOnlyList<Recipient> recipients,
        EmailDefinition email,
        CancellationToken cancellationToken)
    {
        var results = new List<SendRowResult>();

        foreach (var recipient in recipients)
        {
            var result = await SendRowAsync(
                recipient,
                email,
                cancellationToken);

            results.Add(result);
        }

        return results;
    }

    private async Task<SendRowResult> SendRowAsync(
        Recipient recipient,
        EmailDefinition email,
        CancellationToken cancellationToken)
    {
        var recipientEmail =
            GetRecipientEmail(recipient);

        try
        {
            var rendered =
                _renderer.Render(
                    recipient,
                    email);

            if (!rendered.IsValid || rendered.Email is null)
            {
                return SendRowResult.FailedValidation(
                    recipient.RowId,
                    recipientEmail,
                    rendered.Errors);
            }

            var providerResult =
                await _provider.SendAsync(
                    recipientEmail,
                    rendered.Email,
                    cancellationToken);

            if (!providerResult.IsSuccess)
            {
                return SendRowResult.Failed(
                    recipient.RowId,
                    recipientEmail,
                    providerResult.ErrorCode
                        ?? "ProviderError",
                    providerResult.ErrorMessage
                        ?? "The email provider failed to send the message.");
            }

            return SendRowResult.Sent(
                recipient.RowId,
                recipientEmail,
                providerResult.ProviderMessageId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return SendRowResult.Failed(
                recipient.RowId,
                recipientEmail,
                "UnexpectedError",
                ex.Message);
        }
    }

    private static string GetRecipientEmail(
        Recipient recipient)
    {
        return recipient.Values
            .FirstOrDefault(
                pair => pair.Key.Equals(
                    "Email",
                    StringComparison.OrdinalIgnoreCase))
            .Value
            ?? string.Empty;
    }
}