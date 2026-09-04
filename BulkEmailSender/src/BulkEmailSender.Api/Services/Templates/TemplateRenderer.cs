using System.Text.RegularExpressions;
using BulkEmailSender.Api.Domain.Email;
using System.Net;

namespace BulkEmailSender.Api.Services.Template;

public sealed class TemplateRenderer
{
    private static readonly Regex PlaceholderRegex =
        new(
            @"\{([^{}]+)\}",
            RegexOptions.Compiled);

    public IReadOnlySet<string> GetFields(string template)
    {
        return PlaceholderRegex
            .Matches(template)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetMissingFields(
        string template,
        IReadOnlyDictionary<string, string> values)
    {
        var fields = GetFields(template);

        return fields
            .Where(field => !values.ContainsKey(field))
            .ToList();
    }

    public string Render(
    string template,
    IReadOnlyDictionary<string, string> values)
    {
        return PlaceholderRegex.Replace(
            template,
            match =>
            {
                var field = match.Groups[1].Value.Trim();

                return values.TryGetValue(
                    field,
                    out var value)
                    ? WebUtility.HtmlEncode(value)
                    : match.Value;
            });
    }
}