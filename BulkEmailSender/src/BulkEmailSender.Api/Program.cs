using BulkEmailSender.Api.Data;
using Microsoft.EntityFrameworkCore;
using BulkEmailSender.Api.Services.Email;
using BulkEmailSender.Api.Services.Template;
using BulkEmailSender.Api.Services.Validation;
using BulkEmailSender.Api.Contracts;
using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Services.Providers;
using BulkEmailSender.Api.Services.Sending;

using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
// Avoid registering the Windows Event Log provider in environments where the
// API process cannot write to the machine-wide event source.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

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
builder.Services.AddSingleton<IEmailProvider, StubEmailProvider>();
builder.Services.AddSingleton<SendOrchestrator>();
builder.Services.AddScoped<SendOperationStore>();
builder.Services.AddHostedService<SendWorker>();
builder.Services.AddSingleton<ISendQueue, InMemorySendQueue>();
builder.Services.AddSingleton<SendEventBroadcaster>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

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

app.MapGet("/api/health", (
    ILogger<Program> logger) =>
{
    logger.LogInformation("Health endpoint checked.");

    return Results.Ok(new
    {
        status = "healthy"
    });
});

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
        ISendQueue queue,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        // Validate the complete batch before persistence or queueing, avoiding
        // a partially queued send when one recipient or attachment is invalid.
        var errors = validator.Validate(request, out var recipients, out var email);
        if (errors.Count > 0 || email is null)
            return Results.BadRequest(new { errors });

        var operation =
            await operationStore.CreateAsync(
                recipients,
                email,
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
        CancellationToken cancellationToken,
        HttpResponse response) =>
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

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

            var operation = await operationStore.GetAsync(operationId, cancellationToken);
            if (operation is null)
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
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

app.UseSwagger();
app.UseSwaggerUI();
app.Run();


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
public partial class Program;
