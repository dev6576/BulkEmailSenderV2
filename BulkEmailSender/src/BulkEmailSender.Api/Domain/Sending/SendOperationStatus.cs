namespace BulkEmailSender.Api.Domain.Sending;

public enum SendOperationStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}