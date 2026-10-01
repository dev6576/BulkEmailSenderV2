namespace BulkEmailSender.Api.Data.Entities;

public sealed class GoogleOAuthStateEntity
{
    public string StateHash { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
