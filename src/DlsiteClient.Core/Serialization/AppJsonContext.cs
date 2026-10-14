using System.Text.Json.Serialization;
using DlsiteClient.Core.Api;
using DlsiteClient.Core.Models;

namespace DlsiteClient.Core.Serialization;

/// <summary>AOT-safe JSON — no reflection serializer under Native AOT.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(LoginResponseDto))]
[JsonSerializable(typeof(PurchaseListResponseDto))]
[JsonSerializable(typeof(ProductDetailEnvelopeDto))]
[JsonSerializable(typeof(WorkTypeResponseDto))]
public partial class AppJsonContext : JsonSerializerContext;
