namespace BulkEmailSender.Api.Domain.Sending;

public enum SendWorkItemStatus
{
    Pending,
    Sending,
    Sent,
    Failed
}