using System.Net;
using System.Net.Http.Json;
using BulkEmailSender.Api.Contracts;

namespace BulkEmailSender.Api.Tests.Api;

public sealed class PreviewEndpointTests
    : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public PreviewEndpointTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_ValidRecipient_ReturnsRenderedEmail()
    {
        var request = new PreviewRequest
        {
            RowId = 1,
            Values = new Dictionary<string, string>
            {
                ["Email"] = "alice@example.com",
                ["Name"] = "Alice",
                ["Company"] = "Acme"
            },
            Email = new EmailDefinitionRequest
            {
                Subject = "Welcome to {Company}",
                Body = "<p>Hi <strong>{Name}</strong></p>"
            }
        };

        var response = await _client.PostAsJsonAsync(
            "/api/preview",
            request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<PreviewResponse>();

        Assert.NotNull(result);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);

        Assert.NotNull(result.Email);

        Assert.Equal(
            "Welcome to Acme",
            result.Email.Subject);

        Assert.Equal(
            "<p>Hi <strong>Alice</strong></p>",
            result.Email.HtmlBody);
    }

    [Fact]
    public async Task Preview_MissingTemplateField_ReturnsValidationError()
    {
        var request = new PreviewRequest
        {
            RowId = 1,
            Values = new Dictionary<string, string>
            {
                ["Email"] = "alice@example.com",
                ["Name"] = "Alice"
            },
            Email = new EmailDefinitionRequest
            {
                Subject = "Welcome to {Company}",
                Body = "<p>Hi <strong>{Name}</strong></p>"
            }
        };

        var response = await _client.PostAsJsonAsync(
            "/api/preview",
            request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<PreviewResponse>();

        Assert.NotNull(result);

        Assert.False(result.IsValid);
        Assert.Null(result.Email);

        Assert.Contains(
            result.Errors,
            error => error.Code == "MissingTemplateField");
    }

    [Fact]
    public async Task Preview_InvalidEmail_ReturnsValidationError()
    {
        var request = new PreviewRequest
        {
            RowId = 1,
            Values = new Dictionary<string, string>
            {
                ["Email"] = "not-an-email",
                ["Name"] = "Alice",
                ["Company"] = "Acme"
            },
            Email = new EmailDefinitionRequest
            {
                Subject = "Welcome",
                Body = "<p>Hi {Name}</p>"
            }
        };

        var response = await _client.PostAsJsonAsync(
            "/api/preview",
            request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<PreviewResponse>();

        Assert.NotNull(result);

        Assert.False(result.IsValid);
        Assert.Null(result.Email);

        Assert.Contains(
            result.Errors,
            error => error.Code == "InvalidEmail");
    }
}