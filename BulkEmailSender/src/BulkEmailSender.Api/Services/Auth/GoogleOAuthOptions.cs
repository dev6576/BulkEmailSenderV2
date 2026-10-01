namespace BulkEmailSender.Api.Services.Auth;

public sealed class GoogleOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:5016/api/auth/google/callback";
    public string AngularReturnUri { get; set; } = "http://localhost:4200/";
}
