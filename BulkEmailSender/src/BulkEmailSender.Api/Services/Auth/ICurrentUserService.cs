namespace BulkEmailSender.Api.Services.Auth;

public interface ICurrentUserService
{
    string UserId { get; }
    void SetUserId(string userId);
}

public sealed class BrowserSessionUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public const string CookieName = "bulk-email-session";
    private string? _overrideUserId;
    public string UserId => _overrideUserId ?? accessor.HttpContext?.Items[CookieName]?.ToString()
        ?? throw new InvalidOperationException("A browser session is required.");
    public void SetUserId(string userId) => _overrideUserId = userId;
}
