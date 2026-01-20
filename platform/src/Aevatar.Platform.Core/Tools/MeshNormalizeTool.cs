using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.Platform.Core.Workflow;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Aevatar.Platform.Core.Tools;

// ============================================================
//  MeshNormalizeTool
//
//  说明：
//  - 校验并规范化 Cognitive Mesh DSL (v0.1) 输出
//  - 返回 canonical JSON/YAML 供 Hermes 写入
// ============================================================
public sealed class MeshNormalizeTool : AevatarToolBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new StrategyKindJsonConverter()
        }
    };

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .DisableAliases()
        .Build();

    private readonly string _configDirectory;
    private readonly string _workingDirectory;

    public MeshNormalizeTool(string? configDirectory, string? workingDirectory)
    {
        _configDirectory = (configDirectory ?? string.Empty).Trim();
        _workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : workingDirectory.Trim();
    }

    public override string Name => "mesh_normalize";
    public override string Description => "Validate and normalize Cognitive Mesh DSL v0.1 to canonical JSON/YAML.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "mesh", "workflow", "dsl", "normalize" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["content"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Workflow DSL content (YAML or JSON)."
                },
                ["format"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Output format: json or yaml (default: json)."
                },
                ["pretty"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Pretty-print output (default: true)."
                }
            },
            Required = new[] { "content" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetString(parameters, "content", out var content))
        {
            return Task.FromResult<IMessage>(ToStruct(new
            {
                ok = false,
                error = "content_required"
            }));
        }

        var format = "json";
        if (parameters.TryGetValue("format", out var fmtObj))
        {
            var raw = (fmtObj?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(raw))
                format = raw;
        }

        if (format is "yml")
            format = "yaml";

        if (format is not ("json" or "yaml"))
        {
            return Task.FromResult<IMessage>(ToStruct(new
            {
                ok = false,
                error = "format_invalid",
                message = "format must be json or yaml"
            }));
        }

        var pretty = true;
        if (parameters.TryGetValue("pretty", out var prettyObj))
            pretty = TryGetBool(prettyObj);

        var compiler = new PlatformMeshCompiler(
            localAgentRoot: _workingDirectory,
            configAgentsDir: string.IsNullOrWhiteSpace(_configDirectory)
                ? null
                : Path.Combine(_configDirectory, "agents"));

        var compile = compiler.Compile(content);
        if (!compile.Ok || compile.Definition == null)
        {
            var errors = compile.Errors.Select(e => new
            {
                code = e.Code,
                message = e.Message,
                path = e.Path ?? string.Empty
            }).ToArray();

            return Task.FromResult<IMessage>(ToStruct(new
            {
                ok = false,
                error = "mesh_compile_failed",
                errors
            }));
        }

        var json = SerializeJson(compile.Definition, pretty);
        if (format == "json")
        {
            return Task.FromResult<IMessage>(ToStruct(new
            {
                ok = true,
                format = "json",
                normalized = json
            }));
        }

        var yaml = SerializeYaml(json);
        return Task.FromResult<IMessage>(ToStruct(new
        {
            ok = true,
            format = "yaml",
            normalized = yaml
        }));
    }

    private static string SerializeJson(MeshDefinition definition, bool pretty)
    {
        var options = new JsonSerializerOptions(JsonOptions)
        {
            WriteIndented = pretty
        };
        return JsonSerializer.Serialize(definition, options);
    }

    private static string SerializeYaml(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var obj = ToPlainObject(doc.RootElement);
        return YamlSerializer.Serialize(obj ?? new Dictionary<string, object?>());
    }

    private static object? ToPlainObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var prop in element.EnumerateObject())
                    dict[prop.Name] = ToPlainObject(prop.Value);
                return dict;
            }
            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    list.Add(ToPlainObject(item));
                return list;
            }
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l)) return l;
                if (element.TryGetDouble(out var d)) return d;
                return element.GetDecimal();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    private static bool TryGetString(Dictionary<string, object> parameters, string key, out string value)
    {
        value = string.Empty;
        if (!parameters.TryGetValue(key, out var raw) || raw == null)
            return false;
        value = raw.ToString() ?? string.Empty;
        return value.Trim().Length > 0;
    }

    private static bool TryGetBool(object? raw)
    {
        if (raw == null) return false;
        if (raw is bool b) return b;
        return bool.TryParse(raw.ToString(), out var parsed) && parsed;
    }

    private static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonParser.Default.Parse<Struct>(json);
    }

    private sealed class StrategyKindJsonConverter : JsonConverter<StrategyKind>
    {
        public override StrategyKind Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return StrategyKind.Cot;

            var normalized = Normalize(raw);
            return normalized switch
            {
                "cot" => StrategyKind.Cot,
                "tot" => StrategyKind.Tot,
                "got" => StrategyKind.Got,
                "uotcomb" => StrategyKind.UotComb,
                "uotexpl" => StrategyKind.UotExpl,
                "uottrans" => StrategyKind.UotTrans,
                _ => StrategyKind.Cot
            };
        }

        public override void Write(Utf8JsonWriter writer, StrategyKind value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                StrategyKind.Cot => "cot",
                StrategyKind.Tot => "tot",
                StrategyKind.Got => "got",
                StrategyKind.UotComb => "uot_comb",
                StrategyKind.UotExpl => "uot_expl",
                StrategyKind.UotTrans => "uot_trans",
                _ => "cot"
            });
        }

        private static string Normalize(string value)
        {
            Span<char> buffer = stackalloc char[value.Length];
            var index = 0;
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    buffer[index++] = char.ToLowerInvariant(ch);
            }

            return new string(buffer[..index]);
        }
    }
}
