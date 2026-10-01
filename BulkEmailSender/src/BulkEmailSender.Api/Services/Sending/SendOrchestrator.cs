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
    private readonly SendRetryOptions _retryOptions;
    private readonly ILogger<SendOrchestrator> _logger;
    public SendOrchestrator(
        EmailRenderer renderer,
        IEmailProvider provider,
        SendRetryOptions? retryOptions = null,
        ILogger<SendOrchestrator>? logger = null)
    {
        _renderer = renderer;
        _provider = provider;
        _retryOptions = retryOptions ?? new SendRetryOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SendOrchestrator>.Instance;
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

            // Retry only failures explicitly classified as transient; validation
            // and permanent provider failures should not be repeated.
            var maxAttempts = Math.Max(1, _retryOptions.MaxAttempts);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation("Sending row {RowId} to provider. Attempt {Attempt} of {MaxAttempts}.", recipient.RowId, attempt, maxAttempts);
                EmailSendResult providerResult;
                try
                {
                    providerResult = await _provider.SendAsync(recipientEmail, rendered.Email, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (HttpRequestException exception)
                {
                    providerResult = EmailSendResult.Failure("ProviderNetworkError", "Temporary provider network failure.", isTransientFailure: true);
                    _logger.LogWarning(exception, "Transient provider error for row {RowId}, attempt {Attempt}.", recipient.RowId, attempt);
                }
                catch (TimeoutException exception)
                {
                    providerResult = EmailSendResult.Failure("ProviderTimeout", "The provider request timed out.", isTransientFailure: true);
                    _logger.LogWarning(exception, "Provider timeout for row {RowId}, attempt {Attempt}.", recipient.RowId, attempt);
                }

                if (providerResult.IsSuccess)
                {
                    _logger.LogInformation("Row {RowId} sent. ProviderMessageId {ProviderMessageId}.", recipient.RowId, providerResult.ProviderMessageId);
                    return SendRowResult.Sent(recipient.RowId, recipientEmail, providerResult.ProviderMessageId, attempt - 1);
                }

                if (!providerResult.IsTransientFailure || attempt == maxAttempts)
                {
                    _logger.LogWarning("Row {RowId} failed permanently or exhausted retries. ErrorCode {ErrorCode}.", recipient.RowId, providerResult.ErrorCode);
                    return SendRowResult.Failed(recipient.RowId, recipientEmail,
                        providerResult.ErrorCode ?? "ProviderError",
                        providerResult.ErrorMessage ?? "The email provider failed to send the message.", attempt - 1);
                }

                // Exponential backoff spaces repeated provider requests while
                // the cap prevents very long waits; cancellation stops the wait.
                var delayMs = Math.Min(
                    Math.Max(0, _retryOptions.MaxDelayMs),
                    (long)Math.Max(0, _retryOptions.InitialDelayMs) * (1L << Math.Min(attempt - 1, 20)));
                _logger.LogWarning("Transient provider failure for row {RowId}; retrying after {DelayMs} ms. Attempt {Attempt}.", recipient.RowId, delayMs, attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
            }
            return SendRowResult.Failed(recipient.RowId, recipientEmail, "ProviderError", "The email provider failed to send the message.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError("Unexpected send failure for row {RowId}; exception type {ExceptionType}.", recipient.RowId, exception.GetType().Name);
            return SendRowResult.Failed(
                recipient.RowId,
                recipientEmail,
                "UnexpectedError",
                "An unexpected error occurred while sending this recipient.");
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
