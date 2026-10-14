using System.Text.Json;
using System.Text.Json.Serialization;

namespace DlsiteClient.Core.Api;

public sealed class MetaDto
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
}

public sealed class LoginResponseDto
{
    [JsonPropertyName("meta")]
    public MetaDto? Meta { get; set; }

    [JsonPropertyName("data")]
    public LoginDataDto? Data { get; set; }
}

public sealed class LoginDataDto
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("session_name")]
    public string? SessionName { get; set; }

    [JsonPropertyName("login_id")]
    public string? LoginId { get; set; }

    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }
}

public sealed class PurchaseListResponseDto
{
    [JsonPropertyName("meta")]
    public MetaDto? Meta { get; set; }

    [JsonPropertyName("data")]
    public PurchaseListDataDto? Data { get; set; }
}

public sealed class PurchaseListDataDto
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("purchases")]
    public List<ProductDto>? Purchases { get; set; }
}

public sealed class ProductDto
{
    [JsonPropertyName("workno")]
    public string? Workno { get; set; }

    [JsonPropertyName("work_name")]
    public string? WorkName { get; set; }

    [JsonPropertyName("maker_name")]
    public string? MakerName { get; set; }

    [JsonPropertyName("work_type")]
    public string? WorkType { get; set; }

    [JsonPropertyName("site_id")]
    public string? SiteId { get; set; }

    [JsonPropertyName("contents_file_size")]
    public long ContentsFileSize { get; set; }

    [JsonPropertyName("point")]
    public int Point { get; set; }

    [JsonPropertyName("regist_date")]
    public string? RegistDate { get; set; }

    [JsonPropertyName("sales_date")]
    public string? SalesDate { get; set; }

    [JsonPropertyName("update_date")]
    public string? UpdateDate { get; set; }

    [JsonPropertyName("image_urls")]
    public ImageUrlsDto? ImageUrls { get; set; }

    [JsonPropertyName("tags")]
    public JsonElement Tags { get; set; }
}

public sealed class ImageUrlsDto
{
    [JsonPropertyName("main")]
    public string? Main { get; set; }

    [JsonPropertyName("thum")]
    public string? Thum { get; set; }
}

public sealed class ProductDetailEnvelopeDto
{
    [JsonPropertyName("meta")]
    public MetaDto? Meta { get; set; }

    [JsonPropertyName("data")]
    public List<ProductDetailDto>? Data { get; set; }
}

public sealed class ProductDetailDto
{
    [JsonPropertyName("workno")]
    public string? Workno { get; set; }

    [JsonPropertyName("work_name")]
    public string? WorkName { get; set; }

    [JsonPropertyName("intro_s")]
    public string? IntroS { get; set; }

    [JsonPropertyName("maker_name")]
    public string? MakerName { get; set; }

    [JsonPropertyName("contents_file_size")]
    public long ContentsFileSize { get; set; }

    [JsonPropertyName("contents_with_url")]
    public List<ContentsWithUrlDto>? ContentsWithUrl { get; set; }
}

public sealed class ContentsWithUrlDto
{
    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("file_size")]
    public string? FileSize { get; set; }

    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }
}

public sealed class WorkTypeResponseDto
{
    [JsonPropertyName("meta")]
    public MetaDto? Meta { get; set; }

    [JsonPropertyName("data")]
    public WorkTypeDataDto? Data { get; set; }
}

public sealed class WorkTypeDataDto
{
    [JsonPropertyName("all_work_types")]
    public List<WorkTypeDto>? AllWorkTypes { get; set; }
}

public sealed class WorkTypeDto
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
