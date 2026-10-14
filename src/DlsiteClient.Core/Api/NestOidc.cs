using System.Security.Cryptography;
using System.Text;

namespace DlsiteClient.Core.Api;

/// <summary>
/// Nest LoginWindow OIDC (implicit id_token) — reverse-engineered from Nest 2.x.
/// </summary>
public static class NestOidc
{
    public const string ClientId = "dlsite.user";
    public const string Scope = "openid";
    public const string ResponseType = "id_token";
    public const string ResponseMode = "fragment";
    public const string RedirectPath = "/home/login";

    public static Uri RedirectUri { get; } = new(DlsiteApiClient.ApiBase + RedirectPath);

    public static Uri BuildAuthorizeUri(string lang = "ja")
    {
        var sb = new StringBuilder();
        sb.Append(DlsiteApiClient.LoginBase.TrimEnd('/'));
        sb.Append("/oauth2/auth?");
        Append(sb, "lang", string.IsNullOrWhiteSpace(lang) ? "ja" : lang, first: true);
        Append(sb, "response_type", ResponseType);
        Append(sb, "response_mode", ResponseMode);
        Append(sb, "client_id", ClientId);
        Append(sb, "scope", Scope);
        Append(sb, "redirect_uri", RedirectUri.ToString());
        Append(sb, "nonce", RandomAlphaNum(6));
        Append(sb, "state", RandomAlphaNum(12));
        return new Uri(sb.ToString());
    }

    public static bool TryParseCallback(Uri? callback, out string idToken, out string state, out string sessionState)
    {
        idToken = "";
        state = "";
        sessionState = "";
        if (callback is null) return false;

        var fragment = callback.Fragment;
        if (string.IsNullOrEmpty(fragment) || fragment.Length < 2)
            return false;

        var map = ParseForm(fragment.AsSpan(1));
        idToken = map.GetValueOrDefault("id_token", "");
        state = map.GetValueOrDefault("state", "");
        sessionState = map.GetValueOrDefault("session_state", "");
        return idToken.Length > 0 && state.Length > 0 && sessionState.Length > 0;
    }

    private static void Append(StringBuilder sb, string key, string value, bool first = false)
    {
        if (!first) sb.Append('&');
        sb.Append(Uri.EscapeDataString(key));
        sb.Append('=');
        sb.Append(Uri.EscapeDataString(value));
    }

    private static Dictionary<string, string> ParseForm(ReadOnlySpan<char> form)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in form.ToString().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var k = Uri.UnescapeDataString(part[..eq]);
            var v = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            map[k] = v;
        }
        return map;
    }

    private static string RandomAlphaNum(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Span<char> chars = stackalloc char[length];
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < length; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }
}
