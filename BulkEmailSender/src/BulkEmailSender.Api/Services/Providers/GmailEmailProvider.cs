using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Services.Auth;

namespace BulkEmailSender.Api.Services.Providers;

public sealed class GmailEmailProvider(
    GoogleCredentialService credentials,
    IHttpClientFactory clients,
    ILogger<GmailEmailProvider> logger) : IEmailProvider
{
    private sealed class RateGate { public readonly SemaphoreSlim Semaphore = new(1, 1); public DateTimeOffset LastSentAt = DateTimeOffset.MinValue; }
    private static readonly ConcurrentDictionary<string, RateGate> RateGates = new();

    public async Task<EmailSendResult> SendAsync(string recipientEmail, RenderedEmail email, CancellationToken cancellationToken)
    {
        var token = await credentials.GetAccessTokenAsync(cancellationToken);
        if (token is null) return EmailSendResult.Failure("GmailNotConnected", "Connect or reconnect Gmail before sending.");

        var connection = await credentials.GetConnectionAsync(cancellationToken);
        if (connection is null) return EmailSendResult.Failure("GmailNotConnected", "Connect or reconnect Gmail before sending.");
        var gate = RateGates.GetOrAdd(connection.GoogleSubjectId, _ => new RateGate());
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var wait = TimeSpan.FromMilliseconds(1000) - (DateTimeOffset.UtcNow - gate.LastSentAt);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Content = JsonContent.Create(new { raw = Base64Url(Encoding.UTF8.GetBytes(BuildMime(recipientEmail, email))) });
            using var response = await clients.CreateClient("gmail-api").SendAsync(request, cancellationToken);
            gate.LastSentAt = DateTimeOffset.UtcNow;
            if (response.IsSuccessStatusCode)
            {
                using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                return EmailSendResult.Success(result.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null);
            }
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var googleError = ReadGoogleError(body);
            logger.LogWarning("Gmail send API returned HTTP {StatusCode}, reason {Reason}.", (int)response.StatusCode, googleError.Reason ?? "unknown");
            if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                if (IsMailSendingLimit(googleError))
                    return EmailSendResult.Failure("GmailSendingLimit", "Gmail's sending limit has been reached. Wait before sending more email.");
                return EmailSendResult.Failure("GmailRateLimit", "Gmail is temporarily limiting sends. Try again later.", true);
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await credentials.MarkAuthorizationFailedAsync(cancellationToken);
                return EmailSendResult.Failure("GmailAuthorizationFailed", "Gmail authorization failed. Reconnect Gmail and try again.");
            }
            if (googleError.Reason is "dailyLimitExceeded" or "quotaExceeded")
                return EmailSendResult.Failure("GmailSendingLimit", "Gmail's sending limit has been reached. Wait before sending more email.");
            if (googleError.Reason == "accessNotConfigured")
                return EmailSendResult.Failure("GmailApiNotEnabled", "Enable the Gmail API in the Google Cloud project used by this OAuth client.");
            if (googleError.Reason == "domainPolicy")
                return EmailSendResult.Failure("GmailBlockedByDomainPolicy", "Your Google Workspace administrator has blocked Gmail API access for this account.");
            if (googleError.Reason is "insufficientPermissions" or "authError")
                return EmailSendResult.Failure("GmailPermissionMissing", "Gmail send permission is missing. Disconnect Gmail, reconnect it, and approve the requested permission.");
            if (googleError.Reason is "userRateLimitExceeded" or "rateLimitExceeded")
                return EmailSendResult.Failure("GmailRateLimit", "Gmail is temporarily limiting sends. Try again later.", true);
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return EmailSendResult.Failure("GmailInvalidMessage", "Gmail rejected the message format. Check the recipient address, subject, body, and attachments.");
            return EmailSendResult.Failure("GmailSendFailed", "Gmail could not send this message. Check the backend log for the Gmail API error reason.", (int)response.StatusCode >= 500);
        }
        finally { gate.Semaphore.Release(); }
    }

    private static string BuildMime(string recipient, RenderedEmail email)
    {
        var boundary = "=_BulkEmail_" + Guid.NewGuid().ToString("N");
        var sb = new StringBuilder();
        sb.Append("To: ").Append(SafeHeader(recipient)).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(email.Cc))
            sb.Append("Cc: ").Append(SafeHeader(email.Cc)).Append("\r\n");
        sb.Append("Subject: ").Append(EncodeHeader(email.Subject)).Append("\r\nMIME-Version: 1.0\r\n");
        if (email.Attachments.Count == 0)
        {
            sb.Append("Content-Type: text/html; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(EncodeMimeBase64(Encoding.UTF8.GetBytes(email.HtmlBody)));
            return sb.ToString();
        }
        sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append("\"\r\n\r\n");
        sb.Append("--").Append(boundary).Append("\r\nContent-Type: text/html; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(EncodeMimeBase64(Encoding.UTF8.GetBytes(email.HtmlBody))).Append("\r\n");
        foreach (var attachment in email.Attachments)
        {
            var fileName = SafeHeader(attachment.FileName).Replace("\"", "'");
            sb.Append("--").Append(boundary).Append("\r\nContent-Type: ").Append(SafeHeader(attachment.ContentType)).Append("; name=\"").Append(fileName).Append("\"\r\nContent-Disposition: attachment; filename=\"").Append(fileName).Append("\"\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(EncodeMimeBase64(attachment.Content)).Append("\r\n");
        }
        sb.Append("--").Append(boundary).Append("--");
        return sb.ToString();
    }

    private static string SafeHeader(string value) => value.Replace("\r", "").Replace("\n", "");
    private static string EncodeHeader(string value) => "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(SafeHeader(value))) + "?=";
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string EncodeMimeBase64(byte[] value) => Convert.ToBase64String(value, Base64FormattingOptions.InsertLineBreaks);

    private static (string? Reason, string? Message) ReadGoogleError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error)) return (null, null);
            var message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            string? reason = null;
            if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                if (first.TryGetProperty("reason", out var reasonElement)) reason = reasonElement.GetString();
            }
            return (reason, message);
        }
        catch (JsonException) { return (null, null); }
    }

    private static bool IsMailSendingLimit((string? Reason, string? Message) error) =>
        error.Reason == "userRateLimitExceeded" &&
        error.Message?.Contains("sending", StringComparison.OrdinalIgnoreCase) == true;
}
