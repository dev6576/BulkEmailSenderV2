using BulkEmailSender.Api.Data;
using Microsoft.EntityFrameworkCore;
using BulkEmailSender.Api.Services.Email;
using BulkEmailSender.Api.Services.Template;
using BulkEmailSender.Api.Services.Validation;
using BulkEmailSender.Api.Contracts;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Services.Providers;
using BulkEmailSender.Api.Services.Sending;
using BulkEmailSender.Api.Logging;

using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using BulkEmailSender.Api.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
// Avoid registering the Windows Event Log provider in environments where the
// API process cannot write to the machine-wide event source.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var databaseConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
var fileLoggerProvider = new LocalFileLoggerProvider(databaseConnectionString);
builder.Logging.AddProvider(fileLoggerProvider);
builder.Services.AddSingleton(new FrontendFileLogStore(fileLoggerProvider.LogDirectory));

builder.Services.AddSingleton<TemplateRenderer>();
builder.Services.AddSingleton<RecipientValidator>();
builder.Services.AddSingleton<SendRowValidator>();
builder.Services.AddSingleton<EmailSendOptions>(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection("Email").Get<EmailSendOptions>() ?? new());
builder.Services.AddSingleton<SendRetryOptions>(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection("Retry").Get<SendRetryOptions>() ?? new());
builder.Services.AddSingleton<EmailAttachmentRequestValidator>();
builder.Services.AddSingleton<SendRequestValidator>();
builder.Services.AddSingleton<EmailRenderer>();
builder.Services.AddDataProtection();
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}
builder.Services.Configure<GoogleOAuthOptions>(builder.Configuration.GetSection("GoogleOAuth"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("google-oauth");
builder.Services.AddHttpClient("google-api");
builder.Services.AddHttpClient("gmail-api");
builder.Services.AddScoped<ICurrentUserService, BrowserSessionUserService>();
builder.Services.AddScoped<GoogleCredentialService>();
builder.Services.AddScoped<GoogleOAuthService>();
builder.Services.AddScoped<IEmailProvider, GmailEmailProvider>();
builder.Services.AddScoped<SendOrchestrator>();
builder.Services.AddScoped<SendOperationStore>();
builder.Services.AddHostedService<SendWorker>();
builder.Services.AddSingleton<ISendQueue, InMemorySendQueue>();
builder.Services.AddSingleton<SendEventBroadcaster>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(databaseConnectionString));

var app = builder.Build();
if (app.Environment.IsProduction() && string.IsNullOrWhiteSpace(app.Configuration["AppAuth:PasswordHash"]))
    throw new InvalidOperationException("AppAuth:PasswordHash must be configured in Production.");
if (app.Environment.IsProduction())
{
    var requiredGoogleSettings = new[]
    {
        "GoogleOAuth:ClientId", "GoogleOAuth:ClientSecret",
        "GoogleOAuth:RedirectUri", "GoogleOAuth:AngularReturnUri"
    };
    var missingGoogleSettings = requiredGoogleSettings.Where(key => string.IsNullOrWhiteSpace(app.Configuration[key])).ToArray();
    if (missingGoogleSettings.Length > 0)
        throw new InvalidOperationException($"Required Production settings are missing: {string.Join(", ", missingGoogleSettings)}.");
}
var forwardedHeaders = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);
if (app.Environment.IsProduction() || app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.Database.OpenConnectionAsync();
        await using (var command = database.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteScalarAsync();
        }
        await database.Database.CloseConnectionAsync();
        await database.Database.MigrateAsync();
    }
    catch (Exception exception)
    {
        app.Logger.LogCritical(exception, "SQLite initialization or EF migration failed; application startup is stopping.");
        throw;
    }
}
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("Application started. Local log file: {LogFilePath}", fileLoggerProvider.LogFilePath);

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try
    {
        await next();
        logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms.",
            context.Request.Method, context.Request.Path, context.Response.StatusCode,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "HTTP {Method} {Path} failed after {ElapsedMs} ms.",
            context.Request.Method, context.Request.Path,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        throw;
    }
});

var appAuthProtector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("BulkEmailSender.ApplicationLogin.v1");
app.Use(async (context, next) =>
{
    if (string.IsNullOrWhiteSpace(app.Configuration["AppAuth:PasswordHash"]) ||
        !context.Request.Path.StartsWithSegments("/api") || context.Request.Path == "/api/health" ||
        context.Request.Path.StartsWithSegments("/api/auth/login") ||
        context.Request.Path.StartsWithSegments("/api/auth/logout") ||
        context.Request.Path.StartsWithSegments("/api/auth/session") ||
        context.Request.Path == "/api/auth/google/callback")
    {
        await next();
        return;
    }
    try
    {
        var ticket = context.Request.Cookies["bulk-email-auth"] is { } cookie ? appAuthProtector.Unprotect(cookie) : "";
        var parts = ticket.Split('|', 2);
        if (parts.Length == 2 && long.TryParse(parts[1], out var expiry) && expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            await next();
            return;
        }
    }
    catch (System.Security.Cryptography.CryptographicException) { }
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
});

app.MapPost("/api/auth/login", (LoginRequest request, HttpContext context, IConfiguration configuration) =>
{
    var encoded = configuration["AppAuth:PasswordHash"];
    if (string.IsNullOrWhiteSpace(encoded) || !VerifyPassword(request.Password ?? "", encoded))
        return Results.Unauthorized();
    var expiry = DateTimeOffset.UtcNow.AddHours(12);
    context.Response.Cookies.Append("bulk-email-auth", appAuthProtector.Protect($"single-user|{expiry.ToUnixTimeSeconds()}"), new CookieOptions
    {
        HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict,
        IsEssential = true, Expires = expiry, Path = "/"
    });
    return Results.Ok(new { authenticated = true });
});
app.MapPost("/api/auth/logout", (HttpContext context) =>
{
    context.Response.Cookies.Delete("bulk-email-auth", new CookieOptions { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/" });
    return Results.NoContent();
});
app.MapGet("/api/auth/session", (HttpContext context, IConfiguration configuration, IWebHostEnvironment environment) =>
{
    if (!environment.IsProduction() && string.IsNullOrWhiteSpace(configuration["AppAuth:PasswordHash"]))
        return Results.Ok(new { authenticated = true });
    try
    {
        var ticket = context.Request.Cookies["bulk-email-auth"] is { } cookie ? appAuthProtector.Unprotect(cookie) : "";
        var parts = ticket.Split('|', 2);
        return parts.Length == 2 && long.TryParse(parts[1], out var expiry) && expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            ? Results.Ok(new { authenticated = true }) : Results.Unauthorized();
    }
    catch (System.Security.Cryptography.CryptographicException) { return Results.Unauthorized(); }
});

var sessionProtector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("BulkEmailSender.BrowserSession.v1");
app.Use(async (context, next) =>
{
    string? userId = null;
    if (context.Request.Cookies.TryGetValue(BrowserSessionUserService.CookieName, out var protectedId))
    {
        try { userId = sessionProtector.Unprotect(protectedId); } catch (System.Security.Cryptography.CryptographicException) { }
    }
    if (string.IsNullOrWhiteSpace(userId))
    {
        userId = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(BrowserSessionUserService.CookieName, sessionProtector.Protect(userId), new CookieOptions
        {
            HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax,
            IsEssential = true, MaxAge = TimeSpan.FromDays(30), Path = "/"
        });
    }
    context.Items[BrowserSessionUserService.CookieName] = userId;
    await next();
});

// Attachments are Base64 inside JSON, so the default request ceiling may
// reject a valid upload before per-file and combined-size validation runs.
var maxRequestBytes = app.Configuration.GetValue<long>("Email:MaxRequestBodyBytes", 36L * 1024 * 1024);
app.Use(async (context, next) =>
{
    var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = maxRequestBytes;
    await next();
});

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/auth/google", async (GoogleOAuthService oauth, IOptions<GoogleOAuthOptions> options, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(options.Value.ClientId) || string.IsNullOrWhiteSpace(options.Value.ClientSecret))
        return Results.Problem("Google OAuth is not configured on the server.", statusCode: 503);
    return Results.Redirect(await oauth.CreateAuthorizationUrlAsync(ct));
});

app.MapGet("/api/auth/google/callback", async (HttpRequest request, GoogleOAuthService oauth, IOptions<GoogleOAuthOptions> options, ILogger<Program> logger, CancellationToken ct) =>
{
    var destination = options.Value.AngularReturnUri;
    var error = request.Query["error"].ToString();
    if (!string.IsNullOrEmpty(error))
    {
        logger.LogWarning("Google OAuth authorization was denied with error {OAuthError}.", error);
        return Results.Redirect(AddResult(destination, "error", "authorization_denied"));
    }
    var code = request.Query["code"].ToString();
    var state = request.Query["state"].ToString();
    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
    {
        logger.LogWarning("Google OAuth callback did not include the required code and state values.");
        return Results.Redirect(AddResult(destination, "error", "invalid_callback"));
    }
    try
    {
        var success = await oauth.CompleteAsync(code, state, ct);
        if (success) logger.LogInformation("Google OAuth connection completed successfully.");
        else logger.LogWarning("Google OAuth callback was rejected because its state was invalid or expired.");
        return Results.Redirect(AddResult(destination, "gmail", success ? "connected" : "invalid_state"));
    }
    catch (HttpRequestException exception)
    {
        logger.LogError(exception, "Google OAuth token exchange or profile lookup failed.");
        return Results.Redirect(AddResult(destination, "error", "token_exchange_failed"));
    }
    catch (InvalidOperationException exception)
    {
        logger.LogError(exception, "Google OAuth callback could not be completed.");
        return Results.Redirect(AddResult(destination, "error", "token_exchange_failed"));
    }
});

app.MapGet("/api/auth/status", async (GoogleCredentialService credentials, CancellationToken ct) => Results.Ok(await credentials.GetStatusAsync(ct)));
app.MapPost("/api/auth/google/disconnect", async (GoogleCredentialService credentials, CancellationToken ct) =>
{
    await credentials.DisconnectAsync(ct);
    return Results.NoContent();
});

app.MapGet("/health", async (ApplicationDbContext db, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct))
            return Results.Problem("Application database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        return Results.Ok(new { status = "healthy" });
    }
    catch
    {
        return Results.Problem("Application database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/api/client-logs", (ClientLogRequest request, FrontendFileLogStore frontendLogs) =>
{
    if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000 ||
        string.IsNullOrWhiteSpace(request.Level) || request.Level.Length > 16 ||
        (request.Category?.Length ?? 0) > 128)
        return Results.BadRequest();

    var level = request.Level.Trim().ToUpperInvariant() switch
    {
        "WARN" => "WARNING",
        "TRACE" => "TRACE",
        "DEBUG" => "DEBUG",
        "WARNING" => "WARNING",
        "ERROR" => "ERROR",
        _ => "INFORMATION"
    };
    var category = string.IsNullOrWhiteSpace(request.Category) ? "Application" : request.Category.Trim();
    frontendLogs.Write(level, category, request.Message);
    return Results.Accepted();
}).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(8192));

app.MapPost(
    "/api/preview",
    (
        PreviewRequest request,
        EmailRenderer renderer,
        EmailAttachmentRequestValidator attachmentValidator) =>
    {
        // Use the same attachment rules as /api/send so preview and delivery
        // cannot disagree about whether an attachment is acceptable.
        if (!attachmentValidator.TryConvert(request.Email.Attachments, out var attachments, out var attachmentErrors))
        {
            return Results.Ok(new PreviewResponse
            {
                RowId = request.RowId,
                IsValid = false,
                Errors = attachmentErrors.Select(message => new BulkEmailSender.Api.Domain.Validation.ValidationError("InvalidAttachment", message, "Email.Attachments")).ToList()
            });
        }
        var recipient = new Recipient(
            request.RowId,
            request.Values);

        var email = new EmailDefinition
        {
            Subject = request.Email.Subject,
            Body = request.Email.Body,
            Attachments = attachments
        };

        var result = renderer.Render(
            recipient,
            email);

        var response = new PreviewResponse
        {
            RowId = result.RowId,
            IsValid = result.IsValid,
            Errors = result.Errors,
            Email = result.Email is null
                ? null
                : new RenderedEmailResponse
                {
                    Subject = result.Email.Subject,
                    HtmlBody = result.Email.HtmlBody,
                    Attachments = result.Email.Attachments
                        .Select(attachment =>
                            new EmailAttachmentResponse
                            {
                                FileName = attachment.FileName,
                                ContentType = attachment.ContentType
                            })
                        .ToList()
                }
        };

        return Results.Ok(response);
    });
app.MapPost(
    "/api/send",
    async (
        BulkEmailSender.Api.Contracts.SendRequest request,
        SendRequestValidator validator,
        SendOperationStore operationStore,
        GoogleCredentialService credentials,
        ICurrentUserService currentUser,
        ISendQueue queue,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        // Validate the complete batch before persistence or queueing, avoiding
        // a partially queued send when one recipient or attachment is invalid.
        var errors = validator.Validate(request, out var recipients, out var email);
        if (errors.Count > 0 || email is null)
            return Results.BadRequest(new { errors });

        var gmailStatus = await credentials.GetStatusAsync(cancellationToken);
        if (!gmailStatus.Connected)
            return Results.Problem("Connect Gmail before sending email.", statusCode: StatusCodes.Status409Conflict, title: "Gmail not connected");

        var operation =
            await operationStore.CreateAsync(
                recipients,
                email,
                currentUser.UserId,
                cancellationToken);

        logger.LogInformation("Created send operation {OperationId} with {TotalRows} rows.", operation.Id, operation.TotalRows);


        await queue.EnqueueAsync(
            operation.Id,
            cancellationToken);

        logger.LogInformation("Queued send operation {OperationId}.", operation.Id);

        return Results.Accepted(
            $"/api/send/{operation.Id}",
            new SendAcceptedResponse
            {
                OperationId = operation.Id,
                Status =
                    operation.Status.ToString(),
                TotalRows =
                    operation.TotalRows
            });
    });

app.MapGet(
    "/api/send/{operationId:guid}/events",
    async (
        Guid operationId,
        SendEventBroadcaster broadcaster,
        SendOperationStore operationStore,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken,
        HttpResponse response) =>
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        var operation = await operationStore.GetAsync(operationId, cancellationToken);
        if (operation is null || operation.UserId != currentUser.UserId)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Subscribe before replaying saved events: live worker events can arrive
        // during the replay, and this ordering prevents a gap in the SSE stream.
        var subscription =
            broadcaster.Subscribe(operationId);

        try
        {
            // Replay rows already finished before the browser connected, then
            // continue reading from the live subscription below.
            var completedEvents =
                await operationStore
                    .GetCompletedEventsAsync(
                        operationId,
                        cancellationToken);

            foreach (var sendEvent in completedEvents)
            {
                await WriteSseEventAsync(
                    response,
                    sendEvent,
                    cancellationToken);
            }

            if (operation.Status == nameof(BulkEmailSender.Api.Domain.Sending.SendOperationStatus.Completed))
            {
                // Finish reconnects for operations already complete; otherwise
                // the client would wait on a stream that can never receive more.
                await WriteSseEventAsync(response, new SendEvent
                {
                    OperationId = operationId,
                    RowId = 0,
                    Status = "Completed",
                    SentRows = operation.SentRows,
                    FailedRows = operation.FailedRows,
                    TotalRows = operation.TotalRows
                }, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
                return;
            }

            await response.Body.FlushAsync(
                cancellationToken);

            // Now consume live events.
            await foreach (
                var sendEvent in subscription.Reader
                    .ReadAllAsync(cancellationToken))
            {
                await WriteSseEventAsync(
                    response,
                    sendEvent,
                    cancellationToken);

                await response.Body.FlushAsync(
                    cancellationToken);
            }
        }
        finally
        {
            broadcaster.Unsubscribe(
                operationId,
                subscription.SubscriptionId);
        }
    });

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html"));
});

app.UseSwagger();
app.UseSwaggerUI();
app.Run();

static string AddResult(string destination, string key, string value)
{
    var builder = new UriBuilder(destination);
    var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(builder.Query)
        .ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
    query[key] = value;
    builder.Query = string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    return builder.Uri.ToString();
}


static async Task WriteSseEventAsync(
    HttpResponse response,
    SendEvent sendEvent,
    CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(sendEvent);

    await response.WriteAsync(
        $"event: row-update\n" +
        $"data: {json}\n\n",
        cancellationToken);
}

static bool VerifyPassword(string password, string encoded)
{
    try
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations) || iterations < 100000) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, expected.Length);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
    }
    catch (FormatException) { return false; }
}
public sealed record ClientLogRequest(string Level, string? Category, string Message);
public sealed record LoginRequest(string? Password);
public partial class Program;
