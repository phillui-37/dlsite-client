using System.Net;
using DlsiteClient.Core.Services;

namespace DlsiteClient.Core.Api;

/// <summary>
/// Recreates the inner <see cref="HttpClientHandler"/> when proxy settings change.
/// </summary>
internal sealed class SettingsAwareProxyHandler : DelegatingHandler
{
    private readonly SettingsService _settings;
    private readonly object _gate = new();
    private string _key;

    public SettingsAwareProxyHandler(SettingsService settings)
    {
        _settings = settings;
        _key = KeyOf(settings.Current);
        InnerHandler = BuildHandler(settings.Current);
        _settings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        var s = _settings.Current;
        var key = KeyOf(s);
        lock (_gate)
        {
            if (key == _key) return;
            var old = InnerHandler;
            InnerHandler = BuildHandler(s);
            _key = key;
            old?.Dispose();
        }
    }

    private static string KeyOf(Models.AppSettings s) =>
        $"{s.ProxyMode}|{s.ProxyUrl ?? ""}";

    private static HttpClientHandler BuildHandler(Models.AppSettings s)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        switch (s.ProxyMode)
        {
            case "system":
                handler.UseProxy = true;
                handler.Proxy = WebRequest.DefaultWebProxy;
                break;
            case "custom" when !string.IsNullOrWhiteSpace(s.ProxyUrl):
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(s.ProxyUrl);
                break;
            default:
                handler.UseProxy = false;
                break;
        }

        return handler;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _settings.Changed -= OnSettingsChanged;
        base.Dispose(disposing);
    }
}
