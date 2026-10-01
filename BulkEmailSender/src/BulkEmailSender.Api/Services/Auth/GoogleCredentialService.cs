using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BulkEmailSender.Api.Data;
using BulkEmailSender.Api.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BulkEmailSender.Api.Services.Auth;

public sealed record GmailConnectionStatus(bool Connected, string? EmailAddress, string? Error = null);
public sealed record GoogleToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

public sealed class GoogleCredentialService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IDataProtectionProvider protection,
    IHttpClientFactory clients,
    IOptions<GoogleOAuthOptions> options,
    ILogger<GoogleCredentialService> logger)
{
    private readonly IDataProtector _protector = protection.CreateProtector("BulkEmailSender.GoogleOAuth.v1");

    public async Task<GmailConnectionStatus> GetStatusAsync(CancellationToken ct)
    {
        var row = await db.GoogleOAuthConnections.SingleOrDefaultAsync(x => x.UserId == currentUser.UserId, ct);
        if (row is not { IsActive: true, RevokedAtUtc: null })
            return new(false, null, row?.RevokedAtUtc is not null ? "Reconnect Gmail to continue." : null);
        if (row.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            var refreshed = await GetAccessTokenAsync(ct);
            if (refreshed is null) return new(false, null, "Reconnect Gmail to continue.");
        }
        return new(true, row.EmailAddress);
    }

    public async Task<GoogleOAuthConnectionEntity?> GetConnectionAsync(CancellationToken ct) =>
        await db.GoogleOAuthConnections.SingleOrDefaultAsync(x => x.UserId == currentUser.UserId && x.IsActive && x.RevokedAtUtc == null, ct);

    public async Task<GoogleToken?> GetAccessTokenAsync(CancellationToken ct)
    {
        var row = await GetConnectionAsync(ct);
        if (row is null) return null;
        var access = _protector.Unprotect(row.AccessTokenProtected);
        if (row.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2)) return new(access, row.AccessTokenExpiresAtUtc);
        if (string.IsNullOrWhiteSpace(row.RefreshTokenProtected))
        {
            await DeactivateAsync(row, ct);
            return null;
        }

        try
        {
            using var response = await clients.CreateClient("google-oauth").PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.Value.ClientId,
                    ["client_secret"] = options.Value.ClientSecret,
                    ["refresh_token"] = _protector.Unprotect(row.RefreshTokenProtected),
                    ["grant_type"] = "refresh_token"
                }), ct);
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                {
                    await DeactivateAsync(row, ct);
                    return null;
                }
            }
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Google returned an empty token response.");
            if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0)
                throw new InvalidOperationException("Google returned an invalid token response.");
            row.AccessTokenProtected = _protector.Protect(token.AccessToken);
            row.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return new(token.AccessToken, row.AccessTokenExpiresAtUtc);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Google access token refresh failed ({ExceptionType}).", ex.GetType().Name);
            throw;
        }
    }

    public async Task SaveConnectionAsync(string subject, string email, string accessToken, string? refreshToken, int expiresIn, string scope, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.GoogleOAuthConnections.SingleOrDefaultAsync(x => x.UserId == currentUser.UserId, ct);
        if (row is null)
        {
            row = new GoogleOAuthConnectionEntity { UserId = currentUser.UserId, CreatedAtUtc = now };
            db.GoogleOAuthConnections.Add(row);
        }
        row.GoogleSubjectId = subject;
        row.EmailAddress = email;
        row.AccessTokenProtected = _protector.Protect(accessToken);
        if (!string.IsNullOrWhiteSpace(refreshToken)) row.RefreshTokenProtected = _protector.Protect(refreshToken);
        row.AccessTokenExpiresAtUtc = now.AddSeconds(expiresIn);
        row.Scope = scope;
        row.UpdatedAtUtc = now;
        row.RevokedAtUtc = null;
        row.IsActive = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        var row = await GetConnectionAsync(ct);
        if (row is null) return;
        var revocationToken = row.RefreshTokenProtected is null
            ? _protector.Unprotect(row.AccessTokenProtected)
            : _protector.Unprotect(row.RefreshTokenProtected);
        row.IsActive = false;
        row.RevokedAtUtc = DateTimeOffset.UtcNow;
        row.AccessTokenProtected = string.Empty;
        row.RefreshTokenProtected = null;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (!string.IsNullOrEmpty(revocationToken))
        {
            try { await clients.CreateClient("google-oauth").PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = revocationToken }), ct); }
            catch (HttpRequestException ex) { logger.LogInformation("Google token revocation request failed ({ExceptionType}).", ex.GetType().Name); }
        }
    }

    public async Task MarkAuthorizationFailedAsync(CancellationToken ct)
    {
        var row = await GetConnectionAsync(ct);
        if (row is not null) await DeactivateAsync(row, ct);
    }

    private async Task DeactivateAsync(GoogleOAuthConnectionEntity row, CancellationToken ct)
    {
        row.IsActive = false;
        row.RevokedAtUtc = DateTimeOffset.UtcNow;
        row.AccessTokenProtected = string.Empty;
        row.RefreshTokenProtected = null;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }
}

public sealed class GoogleOAuthService(
    ApplicationDbContext db,
    GoogleCredentialService credentials,
    ICurrentUserService currentUser,
    IHttpClientFactory clients,
    IOptions<GoogleOAuthOptions> options)
{
    private static readonly string[] Scopes = ["openid", "email", "profile", "https://www.googleapis.com/auth/gmail.send"];

    public async Task<string> CreateAuthorizationUrlAsync(CancellationToken ct)
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        db.GoogleOAuthStates.Add(new GoogleOAuthStateEntity { StateHash = hash, UserId = currentUser.UserId, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) });
        await db.SaveChangesAsync(ct);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ClientId,
            ["redirect_uri"] = options.Value.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", query.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
    }

    public async Task<bool> CompleteAsync(string code, string state, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        var saved = await db.GoogleOAuthStates.SingleOrDefaultAsync(x => x.StateHash == hash, ct);
        if (saved is null || saved.UserId != currentUser.UserId || saved.ExpiresAtUtc <= DateTimeOffset.UtcNow) return false;
        db.GoogleOAuthStates.Remove(saved);
        await db.SaveChangesAsync(ct);
        using var response = await clients.CreateClient("google-oauth").PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code, ["client_id"] = options.Value.ClientId, ["client_secret"] = options.Value.ClientSecret, ["redirect_uri"] = options.Value.RedirectUri, ["grant_type"] = "authorization_code" }), ct);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct) ?? throw new InvalidOperationException("Google returned an empty token response.");
        if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0)
            throw new InvalidOperationException("Google returned an invalid token response.");
        using var userResponse = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        userResponse.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var identityResponse = await clients.CreateClient("google-api").SendAsync(userResponse, ct);
        identityResponse.EnsureSuccessStatusCode();
        var identity = await identityResponse.Content.ReadFromJsonAsync<UserInfo>(cancellationToken: ct) ?? throw new InvalidOperationException("Google returned no account identity.");
        if (string.IsNullOrWhiteSpace(identity.Sub) || string.IsNullOrWhiteSpace(identity.Email))
            throw new InvalidOperationException("Google returned incomplete account identity.");
        await credentials.SaveConnectionAsync(identity.Sub, identity.Email, token.AccessToken, token.RefreshToken, token.ExpiresIn, token.Scope ?? string.Join(' ', Scopes), ct);
        return true;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }
    private sealed class UserInfo
    {
        [JsonPropertyName("sub")] public string Sub { get; set; } = "";
        [JsonPropertyName("email")] public string Email { get; set; } = "";
    }
}
