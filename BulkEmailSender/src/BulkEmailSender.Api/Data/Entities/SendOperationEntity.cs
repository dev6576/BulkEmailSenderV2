namespace BulkEmailSender.Api.Data.Entities;

public sealed class SendOperationEntity
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string Status { get; set; } = null!;

    public int TotalRows { get; set; }

    public int SentRows { get; set; }

    public int FailedRows { get; set; }

    public string Subject { get; set; } = null!;

    public string Body { get; set; } = null!;

    public string AttachmentsJson { get; set; } = "[]";

    public ICollection<SendWorkItemEntity> WorkItems { get; set; }
        = [];

    public string EmailJson { get; set; } = null!;
}