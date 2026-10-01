using BulkEmailSender.Api.Contracts;
using BulkEmailSender.Api.Domain.Email;
using Microsoft.AspNetCore.StaticFiles;

namespace BulkEmailSender.Api.Services.Validation;

public sealed class EmailAttachmentRequestValidator(EmailSendOptions options)
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public bool TryConvert(
        IReadOnlyList<EmailAttachmentRequest>? requests,
        out IReadOnlyList<EmailAttachment> attachments,
        out IReadOnlyList<string> errors)
    {
        var converted = new List<EmailAttachment>();
        var validationErrors = new List<string>();
        requests ??= [];

        if (requests.Count > options.MaxAttachmentsPerEmail)
            validationErrors.Add($"No more than {options.MaxAttachmentsPerEmail} attachments are allowed.");

        // Track the total as a long so adding several large files cannot wrap
        // around before the configured combined-size limit is checked.
        long totalBytes = 0;
        foreach (var request in requests)
        {
            var fileName = request.FileName?.Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                validationErrors.Add("Attachment filename is required.");
                continue;
            }
            if (fileName.Length > 255 || fileName is "." or ".." ||
                fileName.IndexOfAny(['/', '\\', ':']) >= 0 || fileName.Any(char.IsControl))
            {
                validationErrors.Add($"Attachment filename '{fileName}' is invalid.");
                continue;
            }

            byte[] bytes;
            if (!string.IsNullOrEmpty(request.ContentBase64))
            {
                // Base64 expands bytes by roughly one third. Check that length
                // before decoding so an oversized request cannot force a large
                // extra allocation just to be rejected afterward.
                if (request.ContentBase64.Length > ((long)options.MaxAttachmentSizeBytes + 2) / 3 * 4 + 4)
                {
                    validationErrors.Add($"Attachment '{fileName}' exceeds the per-file size limit.");
                    continue;
                }
                try { bytes = Convert.FromBase64String(request.ContentBase64); }
                catch (FormatException)
                {
                    validationErrors.Add($"Attachment '{fileName}' does not contain valid Base64 data.");
                    continue;
                }
            }
            else
            {
                // Older clients sent JSON byte arrays in `content`; keep that
                // contract working while newer clients use Base64 strings.
                bytes = request.Content ?? [];
            }

            if (bytes.Length == 0)
            {
                validationErrors.Add($"Attachment '{fileName}' is empty.");
                continue;
            }
            if (bytes.Length > options.MaxAttachmentSizeBytes)
            {
                validationErrors.Add($"Attachment '{fileName}' exceeds the per-file size limit.");
                continue;
            }

            totalBytes += bytes.Length;
            if (totalBytes > options.MaxTotalAttachmentSizeBytes)
            {
                validationErrors.Add("The combined attachment size exceeds the total size limit.");
                break;
            }

            // Derive the MIME type from the safe filename so downstream
            // providers receive a useful content type without trusting input.
            var contentType = ContentTypes.TryGetContentType(fileName, out var inferred)
                ? inferred
                : "application/octet-stream";

            converted.Add(new EmailAttachment
            {
                FileName = fileName,
                ContentType = contentType,
                Content = bytes
            });
        }

        attachments = converted;
        errors = validationErrors;
        return validationErrors.Count == 0;
    }
}
