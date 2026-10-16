using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DlsiteClient.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace DlsiteClient.Core.Api;

public sealed class DlsiteApiClient
{
    public const string UserAgent = "DLsiteNest/2.0.28";
    public const string UserAgentWorkTypes = "DLSiteNest/2.0.28";
    public const string ApiBase = "https://www.dlsite.com";
    public const string LoginBase = "https://login.dlsite.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DlsiteApiClient> _logger;
    private string? _sessionId;
    private string? _proxyUrl;
    private string _proxyMode = "off";

    public DlsiteApiClient(IHttpClientFactory httpClientFactory, ILogger<DlsiteApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string? SessionId => _sessionId;
    public bool IsLoggedIn => !string.IsNullOrEmpty(_sessionId);

    public void SetSession(string? sessionId) => _sessionId = sessionId;

    public void ConfigureProxy(string mode, string? proxyUrl)
    {
        _proxyMode = mode;
        _proxyUrl = proxyUrl;
    }

    public async Task<LoginDataDto?> LoginWithIdTokenAsync(
        string idToken,
        string state,
        string sessionState,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/home/api/native-app/nest/login");
        req.Headers.TryAddWithoutValidation("Authorization", idToken);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["state"] = state,
            ["session_state"] = sessionState
        });
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.LoginResponseDto, ct)
            .ConfigureAwait(false);
        if (dto?.Meta?.Code is not (0 or 200) && dto?.Data?.SessionId is null)
            _logger.LogWarning("Login meta code unexpected: {Code}", dto?.Meta?.Code);
        return dto?.Data;
    }

    /// <summary>Dev/test: set SID directly (e.g. after manual OAuth capture).</summary>
    public void LoginWithSessionId(string sessionId) => _sessionId = sessionId;

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBase}/home/api/native-app/nest/login");
        req.Headers.UserAgent.ParseAdd(UserAgent);
        try
        {
            using var res = await SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logout request failed");
        }
        _sessionId = null;
    }

    public async Task<IReadOnlyList<ProductDto>> FetchAllPurchasesAsync(string locale, CancellationToken ct = default)
    {
        var all = new List<ProductDto>();
        var page = 1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await FetchPurchasesPageAsync(locale, page, ct).ConfigureAwait(false);
            if (batch.Count == 0) break;
            all.AddRange(batch);
            if (batch.Count < 50) break; // Nest pages tend to be chunked
            page++;
            if (page > 500) break;
        }
        return all;
    }

    public async Task<IReadOnlyList<ProductDto>> FetchPurchasesPageAsync(
        string locale, int page, CancellationToken ct = default)
    {
        EnsureSession();
        var url = $"{ApiBase}/home/api/native-app/nest/purchases?locale={Uri.EscapeDataString(locale)}&page={page}";
        using var req = CreateAuthedGet(url);
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        EnsureAuthedStatus(res);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.PurchaseListResponseDto, ct)
            .ConfigureAwait(false);
        ThrowIfAuthMeta(dto?.Meta);
        return dto?.Data?.Purchases ?? [];
    }

    public async Task<ProductDetailDto?> GetProductDetailAsync(
        string workno, string locale, CancellationToken ct = default)
    {
        EnsureSession();
        const string fields =
            "age_category,dl_format,file_type,intro_s,machine,options,regist_date,work_name,work_name_kana,work_type,maker_id,maker_name,site_id,image_main,image_thum,contents_file_size,is_reserve_work,workno,contents_with_url,os_types,sales_date,inservice,on_sale";
        var url =
            $"{ApiBase}/home/api/native-app/nest/product/detail?workno={Uri.EscapeDataString(workno)}&locale={Uri.EscapeDataString(locale)}&is_ana=0&fields={Uri.EscapeDataString(fields)}";
        using var req = CreateAuthedGet(url);
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        EnsureAuthedStatus(res);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var detail = ReadProductDetail(doc.RootElement);
        NormalizeContentsUrls(detail);
        return detail;
    }

    /// <summary>
    /// Nest now returns a product array. Older builds returned <c>{ meta, data }</c>.
    /// </summary>
    private static ProductDetailDto? ReadProductDetail(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                return root.Deserialize(AppJsonContext.Default.ListProductDetailDto)?.FirstOrDefault();
            case JsonValueKind.Object:
            {
                var dto = root.Deserialize(AppJsonContext.Default.ProductDetailEnvelopeDto);
                ThrowIfAuthMeta(dto?.Meta);
                return dto?.Data?.FirstOrDefault();
            }
            default:
                throw new JsonException("product detail root is not an object or array");
        }
    }

    private static void NormalizeContentsUrls(ProductDetailDto? detail)
    {
        if (detail?.ContentsWithUrl is null) return;
        foreach (var file in detail.ContentsWithUrl)
        {
            if (file.Url is not null && file.Url.StartsWith("//", StringComparison.Ordinal))
                file.Url = "https:" + file.Url;
        }
    }

    public async Task<IReadOnlyList<WorkTypeDto>> GetWorkTypesAsync(string locale, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/home/api/native-app/nest/all/work/types?locale={Uri.EscapeDataString(locale)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(UserAgentWorkTypes);
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.WorkTypeResponseDto, ct)
            .ConfigureAwait(false);
        return dto?.Data?.AllWorkTypes ?? [];
    }

    /// <summary>Public product.json — tags/genres + pack/DLC probe.</summary>
    public async Task<JsonDocument?> GetProductJsonAsync(string workno, CancellationToken ct = default, string? siteId = null)
    {
        var url = $"{ApiBase}/{StoreSection(siteId)}/api/=/product.json?workno={Uri.EscapeDataString(workno)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(_sessionId))
            req.Headers.TryAddWithoutValidation("Cookie", $"__DLsite_SID={_sessionId};");
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogDebug("product.json {Workno} status {Status}", workno, res.StatusCode);
            return null;
        }
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.Contains("SORRY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("購入できません", StringComparison.Ordinal))
        {
            _logger.LogInformation("Geo-block or soft-fail for product.json {Workno}", workno);
            return null;
        }
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "product.json parse failed for {Workno}", workno);
            return null;
        }
    }

    private static string StoreSection(string? siteId) =>
        !string.IsNullOrWhiteSpace(siteId) && siteId.All(char.IsAsciiLetter) ? siteId : "maniax";

    public async Task<string?> GetProductPageAsync(string workno, string? siteId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{ApiBase}/{StoreSection(siteId)}/work/=/product_id/{Uri.EscapeDataString(workno)}.html");
        req.Headers.UserAgent.ParseAdd(UserAgent);
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public HttpClient CreateDownloadClient()
    {
        var handler = CreateHandler();
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromHours(6)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(_sessionId))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"__DLsite_SID={_sessionId};");
        return client;
    }

    private HttpRequestMessage CreateAuthedGet(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.TryAddWithoutValidation("Cookie", $"__DLsite_SID={_sessionId};");
        return req;
    }

    private void EnsureSession()
    {
        if (string.IsNullOrEmpty(_sessionId))
            throw new InvalidOperationException("Not logged in");
    }

    /// <summary>Throw when Nest rejects the session cookie.</summary>
    public static void EnsureAuthedStatus(HttpResponseMessage res)
    {
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new NestSessionExpiredException(
                String.Format("Nest session rejected (HTTP {0})", (int)res.StatusCode));
        }
    }

    private static void ThrowIfAuthMeta(MetaDto? meta)
    {
        if (meta is null) return;
        if (meta.Code is 401 or 403)
        {
            throw new NestSessionExpiredException(
                String.Format("Nest session rejected (meta.code {0})", meta.Code));
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("dlsite");
        return await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private HttpMessageHandler CreateHandler()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = true
        };
        ApplyProxy(handler);
        return handler;
    }

    private void ApplyProxy(HttpClientHandler handler)
    {
        switch (_proxyMode)
        {
            case "system":
                handler.UseProxy = true;
                handler.Proxy = WebRequest.DefaultWebProxy;
                break;
            case "custom" when !string.IsNullOrWhiteSpace(_proxyUrl):
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(_proxyUrl);
                break;
            default:
                handler.UseProxy = false;
                break;
        }
    }
}
