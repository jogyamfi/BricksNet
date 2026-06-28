using System.Text.Json.Serialization;

namespace BricksNet.Backends.Rest.Dto;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the SQL Statement Execution API
/// DTOs. Using source generation keeps the REST backend trim- and AOT-friendly by avoiding
/// reflection-based (de)serialisation. Property names use <c>snake_case</c> and
/// <see langword="null"/> properties are omitted on write.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExecuteStatementRequest))]
[JsonSerializable(typeof(ExecuteStatementResponse))]
[JsonSerializable(typeof(ResultData))]
internal sealed partial class SqlExecJsonContext : JsonSerializerContext
{
}
