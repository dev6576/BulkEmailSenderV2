namespace BulkEmailSender.Api.Domain.Email;

public sealed class Recipient
{
    public Recipient(
        long rowId,
        Dictionary<string, string> values)
    {
        RowId = rowId;
        Values = new Dictionary<string, string>(
            values,
            StringComparer.OrdinalIgnoreCase);
    }

    public long RowId { get; }

    public Dictionary<string, string> Values { get; }
}