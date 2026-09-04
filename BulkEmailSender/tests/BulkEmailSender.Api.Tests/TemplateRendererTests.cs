using BulkEmailSender.Api.Services.Template;

namespace BulkEmailSender.Api.Tests.Services.Template;

public class TemplateRendererTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void GetFields_ReturnsTemplateFields()
    {
        var fields = _renderer.GetFields(
            "Hi {Name}, welcome to {Company}");

        Assert.Equal(
            ["Name", "Company"],
            fields);
    }

    [Fact]
    public void GetFields_IsCaseInsensitive()
    {
        var fields = _renderer.GetFields(
            "Hi {Name}, {name}, {NAME}");

        Assert.Single(fields);
        Assert.Contains("Name", fields);
    }

    [Fact]
    public void GetFields_IgnoresDuplicateFields()
    {
        var fields = _renderer.GetFields(
            "{Name} {Name} {Company}");

        Assert.Equal(2, fields.Count);
    }

    [Fact]
    public void GetMissingFields_ReturnsFieldsNotInRow()
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Alice"
        };

        var missing = _renderer.GetMissingFields(
            "Hi {Name}, welcome to {Company}",
            values);

        Assert.Single(missing);
        Assert.Equal("Company", missing[0]);
    }

    [Fact]
    public void GetMissingFields_IsCaseInsensitive()
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Alice",
            ["Company"] = "Acme"
        };

        var missing = _renderer.GetMissingFields(
            "Hi {name}, welcome to {COMPANY}",
            values);

        Assert.Empty(missing);
    }

    [Fact]
    public void Render_ReplacesFields()
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Alice",
            ["Company"] = "Acme"
        };

        var result = _renderer.Render(
            "Hi {Name}, welcome to {Company}",
            values);

        Assert.Equal(
            "Hi Alice, welcome to Acme",
            result);
    }

    [Fact]
    public void Render_IsCaseInsensitive()
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Alice"
        };

        var result = _renderer.Render(
            "Hello {name}",
            values);

        Assert.Equal("Hello Alice", result);
    }

    [Fact]
    public void Render_HtmlEncodesRecipientValues()
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Alice & Bob"
        };

        var result = _renderer.Render(
            "<p>Hi {Name}</p>",
            values);

        Assert.Equal(
            "<p>Hi Alice &amp; Bob</p>",
            result);
    }

    [Fact]
    public void Render_HtmlEncodesHtmlFromRecipientData()
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "<script>alert('x')</script>"
        };

        var result = _renderer.Render(
            "<p>Hi {Name}</p>",
            values);

        Assert.DoesNotContain(
            "<script>",
            result);

        Assert.Contains(
            "&lt;script&gt;",
            result);
    }

    [Fact]
    public void Render_DoesNotEncodeTemplateHtml()
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Alice"
        };

        var result = _renderer.Render(
            "<p><strong>Hi {Name}</strong></p>",
            values);

        Assert.Equal(
            "<p><strong>Hi Alice</strong></p>",
            result);
    }
}