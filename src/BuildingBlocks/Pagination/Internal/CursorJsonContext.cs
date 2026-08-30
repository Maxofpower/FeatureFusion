using System.Text.Json.Serialization;

namespace BuildingBlocks.Pagination;

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CursorPayload))]
internal partial class CursorJsonContext : JsonSerializerContext;
