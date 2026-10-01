namespace BulkEmailSender.Api.Data.Entities;

public sealed class GoogleOAuthConnectionEntity
{
    public string UserId { get; set; } = null!;
    public string GoogleSubjectId { get; set; } = null!;
    public string EmailAddress { get; set; } = null!;
    public string AccessTokenProtected { get; set; } = null!;
    public string? RefreshTokenProtected { get; set; }
    public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }
    public string Scope { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public bool IsActive { get; set; }
}
