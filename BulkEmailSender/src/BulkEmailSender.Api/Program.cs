using BulkEmailSender.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

app.Run();

public partial class Program;