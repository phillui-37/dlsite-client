using System.Net;
using System.Text.RegularExpressions;

namespace DlsiteClient.Core.Services;

public static class ProductPageTags
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    public static IReadOnlyList<string> Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > 4_000_000) return [];
        // Only product genre blocks. Do not import navigation/recommendation tags.
        var options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match block in Regex.Matches(html, """<div\b[^>]*\bclass\s*=\s*["'][^"']*\bmain_genre\b[^"']*["'][^>]*>(?<body>.*?)</div\s*>""", options, Timeout))
            foreach (Match link in Regex.Matches(block.Groups["body"].Value, """<a\b[^>]*\bhref\s*=\s*["'](?<href>[^"']+)["'][^>]*>(?<name>.*?)</a\s*>""", options, Timeout))
            {
                if (!WebUtility.HtmlDecode(link.Groups["href"].Value).Contains("/genre/", StringComparison.OrdinalIgnoreCase)) continue;
                var name = WebUtility.HtmlDecode(Regex.Replace(link.Groups["name"].Value, "<[^>]+>", "", options, Timeout)).Trim();
                if (name.Length > 0) names.Add(name);
            }
        return names.ToList();
    }
}
