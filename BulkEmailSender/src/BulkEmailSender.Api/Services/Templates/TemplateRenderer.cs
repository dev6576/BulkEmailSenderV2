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
            .Where(field => !TryGetValue(values, field, out _))
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

                // Bodies are HTML, so encode spreadsheet values before they
                // are inserted to keep recipient data from becoming markup.
                return TryGetValue(values,
                    field,
                    out var value)
                    ? WebUtility.HtmlEncode(value)
                    : match.Value;
            });
    }

    public string RenderText(string template, IReadOnlyDictionary<string, string> values)
    {
        // Subjects are plain text; HTML encoding here would show entities such
        // as &amp; to recipients instead of the intended character.
        return PlaceholderRegex.Replace(template, match =>
        {
            var field = match.Groups[1].Value.Trim();
            return TryGetValue(values, field, out var value) ? value : match.Value;
        });
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, string> values, string field, out string value)
    {
        if (values.TryGetValue(field, out value!)) return true;
        var pair = values.FirstOrDefault(entry => entry.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
        value = pair.Value!;
        return pair.Key is not null;
    }
}
