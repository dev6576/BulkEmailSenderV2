namespace BulkEmailSender.Api.Services.Validation;

public sealed class EmailSendOptions
{
    public int MaxRecipientsPerOperation { get; set; } = 1000;
    public int MaxAttachmentsPerEmail { get; set; } = 10;
    public int MaxAttachmentSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxTotalAttachmentSizeBytes { get; set; } = 25 * 1024 * 1024;
}
