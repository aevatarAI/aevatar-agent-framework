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

