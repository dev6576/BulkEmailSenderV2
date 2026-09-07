using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Services.Providers;

namespace BulkEmailSender.Api.Tests.Services.Providers;

public sealed class StubEmailProviderTests
{
    [Fact]
    public async Task SendAsync_ReturnsSuccess()
    {
        var provider = new StubEmailProvider();

        var email = new RenderedEmail
        {
            Subject = "Hello",
            HtmlBody = "<p>Hello Alice</p>"
        };

        var result = await provider.SendAsync(
            "alice@example.com",
            email,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ProviderMessageId);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }
}