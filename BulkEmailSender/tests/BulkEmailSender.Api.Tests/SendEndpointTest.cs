using System.Net;
using System.Net.Http.Json;
using BulkEmailSender.Api.Contracts;
using BulkEmailSender.Api.Data;
using BulkEmailSender.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BulkEmailSender.Api.Services.Sending;
using BulkEmailSender.Api.Tests.Services.Sending;

namespace BulkEmailSender.Api.Tests.Api;

public sealed class SendEndpointTests
    : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public SendEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Send_ValidRows_CreatesQueuedOperation()
    {
        var request = new SendRequest
        {
            Recipients =
            [
                new SendRecipientRequest
                {
                    RowId = 1,
                    Values = new Dictionary<string, string>
                    {
                        ["Email"] = "alice@example.com",
                        ["Name"] = "Alice",
                        ["Company"] = "Acme"
                    }
                },
                new SendRecipientRequest
                {
                    RowId = 2,
                    Values = new Dictionary<string, string>
                    {
                        ["Email"] = "bob@example.com",
                        ["Name"] = "Bob",
                        ["Company"] = "Acme"
                    }
                }
            ],

            Email = new EmailDefinitionRequest
            {
                Subject = "Welcome to {Company}",
                Body = "<p>Hi {Name}</p>"
            }
        };

        var response =
            await _client.PostAsJsonAsync(
                "/api/send",
                request);

        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);

        var result =
            await response.Content
                .ReadFromJsonAsync<
                    SendAcceptedResponse>();

        Assert.NotNull(result);

        Assert.NotEqual(
            Guid.Empty,
            result.OperationId);

        Assert.Equal(
            "Queued",
            result.Status);

        Assert.Equal(
            2,
            result.TotalRows);

        using var scope =
            _factory.Services.CreateScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<
                    ApplicationDbContext>();

        var operation =
            await db.SendOperations
                .SingleAsync(
                    x => x.Id == result.OperationId);

        Assert.Equal(
            "Queued",
            operation.Status);

        Assert.Equal(
            2,
            operation.TotalRows);

        var workItems =
            await db.SendWorkItems
                .Where(
                    x => x.OperationId ==
                        result.OperationId)
                .OrderBy(x => x.RowId)
                .ToListAsync();

        Assert.Equal(
            2,
            workItems.Count);

        Assert.All(
            workItems,
            item =>
                Assert.Equal(
                    "Pending",
                    item.Status));

        Assert.Equal(
            1,
            workItems[0].RowId);

        Assert.Equal(
            2,
            workItems[1].RowId);
    }


    [Fact]
    public async Task Send_ValidRows_QueuesOperation()
    {
        var request = new SendRequest
        {
            Recipients =
            [
                new SendRecipientRequest
                {
                    RowId = 1,
                    Values = new Dictionary<string, string>
                    {
                        ["Email"] = "alice@example.com",
                        ["Name"] = "Alice",
                        ["Company"] = "Acme"
                    }
                }
            ],

            Email = new EmailDefinitionRequest
            {
                Subject = "Welcome to {Company}",
                Body = "<p>Hi {Name}</p>"
            }
        };

        var response =
            await _client.PostAsJsonAsync(
                "/api/send",
                request);

        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);

        var result =
            await response.Content
                .ReadFromJsonAsync<SendAcceptedResponse>();

        Assert.NotNull(result);

        Assert.NotEqual(
            Guid.Empty,
            result.OperationId);

        var queue =
            _factory.Services
                .GetRequiredService<TestSendQueue>();

        var queueAgain =
            _factory.Services
                .GetRequiredService<ISendQueue>();

        Assert.Same(
            queue,
            queueAgain);
            
        using var cts =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(1));

        await using var enumerator =
            queue.ReadAllAsync(cts.Token)
                .GetAsyncEnumerator();

        Assert.True(
            await enumerator.MoveNextAsync());

        Assert.Equal(
            result.OperationId,
            enumerator.Current);
    }
}