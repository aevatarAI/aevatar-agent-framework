/*aevatar_tool
{
  "name": "get_env",
  "description": "Get an environment variable by key",
  "category": "Core",
  "version": "1.0.0",
  "tags": ["dotnet", "file", "skill", "env"],
  "parameters": {
    "required": ["key"],
    "items": {
      "key": { "type": "string", "description": "Environment variable name" }
    }
  }
}
*/

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var jsonOptions = new JsonSerializerOptions
{
    // .NET 10 file-based apps may disable reflection serialization by default.
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

var input = await Console.In.ReadToEndAsync();
var inputArgs = string.IsNullOrWhiteSpace(input)
    ? new Dictionary<string, JsonElement>()
    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input, jsonOptions)
      ?? new Dictionary<string, JsonElement>();

string? key = null;
if (inputArgs.TryGetValue("key", out var keyEl) && keyEl.ValueKind == JsonValueKind.String)
{
    key = keyEl.GetString();
}

var value = string.IsNullOrWhiteSpace(key) ? null : Environment.GetEnvironmentVariable(key);

var result = new
{
    key,
    exists = value != null,
    value
};

Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));


