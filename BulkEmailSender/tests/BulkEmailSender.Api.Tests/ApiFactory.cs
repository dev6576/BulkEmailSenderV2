using BulkEmailSender.Api.Data;
using BulkEmailSender.Api.Services.Sending;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BulkEmailSender.Api.Tests.Services.Sending;

namespace BulkEmailSender.Api.Tests;

public sealed class ApiFactory
    : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public ApiFactory()
    {
        _connection =
            new SqliteConnection(
                "Data Source=:memory:");

        _connection.Open();
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace the production SQLite DbContext
            // with our test SQLite connection.
            services.RemoveAll<
                DbContextOptions<ApplicationDbContext>>();

            services.AddDbContext<ApplicationDbContext>(
                options =>
                    options.UseSqlite(_connection));

            // Replace the production queue with a queue
            // that the test can inspect.
            services.RemoveAll<ISendQueue>();

            services.AddSingleton<TestSendQueue>();

            services.AddSingleton<ISendQueue>(
                serviceProvider =>
                    serviceProvider
                        .GetRequiredService<TestSendQueue>());

            // The API tests should not have the real worker
            // consuming the queue.
            services.RemoveAll<
                Microsoft.Extensions.Hosting.IHostedService>();

            // Create the schema in the in-memory SQLite DB.
            using var scope =
                services.BuildServiceProvider()
                    .CreateScope();

            var db =
                scope.ServiceProvider
                    .GetRequiredService<ApplicationDbContext>();

            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
        }

        base.Dispose(disposing);
    }
}
