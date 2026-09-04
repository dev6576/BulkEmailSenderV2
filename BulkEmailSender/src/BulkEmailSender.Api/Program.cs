using BulkEmailSender.Api.Data;
using Microsoft.EntityFrameworkCore;
using BulkEmailSender.Api.Services.Email;
using BulkEmailSender.Api.Services.Template;
using BulkEmailSender.Api.Services.Validation;
using BulkEmailSender.Api.Contracts;
using BulkEmailSender.Api.Domain.Email;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TemplateRenderer>();
builder.Services.AddSingleton<RecipientValidator>();
builder.Services.AddSingleton<SendRowValidator>();
builder.Services.AddSingleton<EmailRenderer>();

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

app.UseSwagger();
app.UseSwaggerUI();
app.Run();

public partial class Program;