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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TemplateRenderer>();
builder.Services.AddSingleton<RecipientValidator>();
builder.Services.AddSingleton<SendRowValidator>();
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
        EmailRenderer renderer) =>
    {
        var recipient = new Recipient(
            request.RowId,
            request.Values);

        var email = new EmailDefinition
        {
            Subject = request.Email.Subject,
            Body = request.Email.Body,
            Attachments = request.Email.Attachments
                .Select(attachment =>
                    new EmailAttachment
                    {
                        FileName = attachment.FileName,
                        ContentType = attachment.ContentType,
                        Content = attachment.Content
                    })
                .ToList()
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
        SendRowValidator validator,
        SendOperationStore operationStore,
        ISendQueue queue,
        CancellationToken cancellationToken) =>
    {
        var recipients =
            request.Recipients
                .Select(recipient =>
                    new Recipient(
                        recipient.RowId,
                        recipient.Values))
                .ToList();

        var email =
            new EmailDefinition
            {
                Subject = request.Email.Subject,
                Body = request.Email.Body,
                Attachments = request.Email.Attachments
                    .Select(attachment =>
                        new EmailAttachment
                        {
                            FileName =
                                attachment.FileName,
                            ContentType =
                                attachment.ContentType,
                            Content =
                                attachment.Content
                        })
                    .ToList()
            };

        foreach (var recipient in recipients)
        {
            var validation =
                validator.Validate(
                    recipient,
                    email);

            if (!validation.IsValid)
            {
                return Results.BadRequest(
                    new
                    {
                        rowId = recipient.RowId,
                        errors = validation.Errors
                    });
            }
        }

        var operation =
            await operationStore.CreateAsync(
                recipients,
                email,
                cancellationToken);

        await queue.EnqueueAsync(
            operation.Id,
            cancellationToken);

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

        // Subscribe FIRST so no live events can be missed
        var subscription =
            broadcaster.Subscribe(operationId);

        try
        {
            // Replay terminal events from the database.
            // Only Sent / Failed rows are returned.
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