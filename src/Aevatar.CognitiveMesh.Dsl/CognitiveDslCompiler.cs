using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;
using Aevatar.CognitiveMesh.Dsl.Validation;
using Aevatar.CognitiveMesh.Dsl.Validation.Rules;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.CognitiveMesh.Dsl;

/// <summary>
/// Responsible for parsing and validating Cognitive Mesh DSL documents.
/// </summary>
public sealed class CognitiveDslCompiler
{
    private readonly CognitiveDslOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IReadOnlyList<IMeshSemanticRule> _rules;

    public CognitiveDslCompiler(CognitiveDslOptions? options = null, IEnumerable<IMeshSemanticRule>? additionalRules = null)
    {
        _options = options ?? CognitiveDslOptions.Default;
        _serializerOptions = CreateSerializerOptions();
        _rules = BuildRules(_options, additionalRules);
    }

    public MeshDefinition Compile(string jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            throw new ArgumentException("DSL 内容不能为空。", nameof(jsonPayload));
        }

        MeshDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<MeshDefinition>(jsonPayload, _serializerOptions)
                ?? throw new DslCompilationException("DSL 解析结果为空。");
        }
        catch (JsonException ex)
        {
            throw new DslCompilationException("无法解析 DSL JSON。", ex);
        }

        Validate(definition);
        return Normalize(definition);
    }

    public MeshDefinition Compile(ReadOnlySpan<byte> utf8Payload)
    {
        var json = Encoding.UTF8.GetString(utf8Payload);
        return Compile(json);
    }

    public async Task<MeshDefinition> CompileAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var json = document.RootElement.GetRawText();
        return Compile(json);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters =
            {
                new StrategyKindConverter(),
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

    private static IReadOnlyList<IMeshSemanticRule> BuildRules(
        CognitiveDslOptions options,
        IEnumerable<IMeshSemanticRule>? additionalRules)
    {
        var builtIn = new IMeshSemanticRule[]
        {
            new UniqueNodeIdRule(),
            new EdgesReferenceExistingNodesRule(),
            new AllowedAgentTypeRule(options),
            new AllowedConstraintRule(options),
            new TransformativeStrategyRule(options)
        };

        return additionalRules is null
            ? builtIn
            : builtIn.Concat(additionalRules).ToArray();
    }

    private static MeshDefinition Normalize(MeshDefinition definition)
    {
        // Defensive copy to guarantee immutability from external references.
        return definition with
        {
            Nodes = definition.Nodes.ToList().AsReadOnly(),
            Edges = definition.Edges.ToList().AsReadOnly(),
            Constraints = definition.Constraints.ToList().AsReadOnly()
        };
    }

    private void Validate(MeshDefinition definition)
    {
        var structuralErrors = MeshDefinitionValidator.Validate(definition);
        var semanticErrors = _rules.SelectMany(r => r.Validate(definition));
        var allErrors = structuralErrors.Concat(semanticErrors).ToArray();

        if (allErrors.Length > 0)
        {
            throw new DslCompilationException("DSL 校验失败", allErrors);
        }
    }
}

public static class MeshInputCoercer
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static (string Json, IReadOnlySet<string> NodeTypes) CoerceToJson(string raw)
    {
        raw = (raw ?? string.Empty).Replace("\r", "").Trim();
        if (raw.Length == 0)
            throw new ArgumentException("DSL 内容不能为空。", nameof(raw));

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
                if (double.TryParse(t, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var d))
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
                return value.ToString();
        }
    }
}

