namespace BulkEmailSender.Api.Contracts;

public sealed class PreviewRequest
{
    public required long RowId { get; init; }

    public required Dictionary<string, string> Values { get; init; }

    public required EmailDefinitionRequest Email { get; init; }
}