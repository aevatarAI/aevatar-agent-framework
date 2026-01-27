using System.Text.Json;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.CognitiveMesh.Dsl;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;
using Aevatar.CognitiveMesh.Dsl.Validation;
using YamlDotNet.Serialization;

namespace Aevatar.Workshop;

public sealed record MeshCompileResult(
    bool Ok,
    MeshDefinition? Definition,
    IReadOnlyList<DslValidationError> Errors,
    string? NormalizedJson)
{
    public static MeshCompileResult Success(MeshDefinition def, string json) => new(true, def, Array.Empty<DslValidationError>(), json);
    public static MeshCompileResult Failed(params DslValidationError[] errors) => new(false, null, errors ?? Array.Empty<DslValidationError>(), null);
}

public sealed class WorkshopMeshCompiler
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ILogger<WorkshopMeshCompiler> _logger;
    private readonly GlobalAgentYamlRegistry _roles;

    public WorkshopMeshCompiler(ILogger<WorkshopMeshCompiler> logger, GlobalAgentYamlRegistry roles)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    public MeshCompileResult Compile(string raw)
    {
        raw = (raw ?? string.Empty).Replace("\r", "").Trim();
        if (raw.Length == 0)
        {
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.empty",
                Message: "workflow YAML 为空。",
                Path: null));
        }

        string json;
        IReadOnlySet<string> nodeTypes;
        try
        {
            (json, nodeTypes) = CoerceToJson(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow YAML -> JSON failed.");
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.coerce_failed",
                Message: ex.Message,
                Path: null));
        }

        var options = CognitiveDslOptions.Default.With(
            allowedAgentTypes: MergeAllowedAgentTypes(nodeTypes),
            allowedConstraintTypes: CognitiveDslOptions.Default.AllowedConstraintTypes,
            metaAgentTypeName: "meta");

        var compiler = new CognitiveDslCompiler(options);
        try
        {
            var def = compiler.Compile(json);
            return MeshCompileResult.Success(def, json);
        }
        catch (DslCompilationException ex)
        {
            var errors = ex.Errors?.ToArray() ?? Array.Empty<DslValidationError>();
            if (errors.Length == 0)
            {
                errors =
                [
                    new DslValidationError("mesh.compile_failed", ex.Message, null)
                ];
            }

            return new MeshCompileResult(false, null, errors, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow compile failed.");
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.compile_exception",
                Message: ex.Message,
                Path: null));
        }
    }

    private IReadOnlySet<string> MergeAllowedAgentTypes(IReadOnlySet<string> nodeTypes)
    {
        var set = new HashSet<string>(CognitiveDslOptions.Default.AllowedAgentTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var role in _roles.GetKnownRoles())
            set.Add(role);
        foreach (var t in nodeTypes)
            set.Add(t);
        set.Add("meta");
        return set;
    }

    private static (string json, IReadOnlySet<string> nodeTypes) CoerceToJson(string raw)
    {
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            using var doc = JsonDocument.Parse(raw);
            var nodeTypes = ExtractNodeTypesFromJson(doc.RootElement);
            return (raw, nodeTypes);
        }

        var obj = Yaml.Deserialize<object>(raw);
        var normalizedYaml = NormalizeYaml(obj);
        var jsonText = JsonSerializer.Serialize(normalizedYaml, Json);
        JsonDocument.Parse(jsonText);
        return (jsonText, ExtractNodeTypes(normalizedYaml));
    }

    private static IReadOnlySet<string> ExtractNodeTypesFromJson(JsonElement root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object)
            return set;

        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            return set;

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object)
                continue;
            if (node.TryGetProperty("type", out var typeElem) && typeElem.ValueKind == JsonValueKind.String)
            {
                var t = (typeElem.GetString() ?? string.Empty).Trim();
                if (t.Length > 0) set.Add(t);
            }
        }

        return set;
    }

    private static IReadOnlySet<string> ExtractNodeTypes(object? normalized)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (normalized is not Dictionary<string, object?> dict)
            return set;

        if (!dict.TryGetValue("nodes", out var nodesObj) || nodesObj is not IEnumerable<object?> nodes)
            return set;

        foreach (var node in nodes)
        {
            if (node is not Dictionary<string, object?> nodeDict)
                continue;

            if (nodeDict.TryGetValue("type", out var typeObj) && typeObj is string type)
            {
                var t = type.Trim();
                if (t.Length > 0) set.Add(t);
            }
        }

        return set;
    }

    private static object? NormalizeYaml(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
            {
                var t = s.Trim();
                if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                if (t is "0.1" or "0.2") return t;
                if (int.TryParse(t, out var i)) return i;
                if (long.TryParse(t, out var l)) return l;
                if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return d;
                return s;
            }
            case bool b:
                return b;
            case int i:
                return i;
            case long l:
                return l;
            case double d:
                return d;
            case float f:
                return f;
            case decimal m:
                return (double)m;
            case System.Collections.IDictionary dict:
            {
                var next = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    var k = (kv.Key?.ToString() ?? string.Empty).Trim();
                    if (k.Length == 0) continue;
                    next[k] = NormalizeYaml(kv.Value);
                }
                return next;
            }
            case System.Collections.IEnumerable seq when value is not string:
            {
                var list = new List<object?>();
                foreach (var item in seq)
                    list.Add(NormalizeYaml(item));
                return list;
            }
            default:
                return value;
        }
    }
}
