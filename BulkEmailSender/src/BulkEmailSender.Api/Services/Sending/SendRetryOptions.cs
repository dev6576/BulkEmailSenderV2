namespace BulkEmailSender.Api.Services.Sending;

public sealed class SendRetryOptions
{
    public int MaxAttempts { get; set; } = 3;
    public int InitialDelayMs { get; set; } = 500;
    public int MaxDelayMs { get; set; } = 10_000;
}
