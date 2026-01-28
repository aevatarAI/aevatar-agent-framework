using Microsoft.Extensions.Logging;
using Aevatar.CognitiveMesh.Dsl;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;
using Aevatar.CognitiveMesh.Dsl.Validation;
using Aevatar.Agents.AI.Core.Configuration;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace Aevatar.VibeResearching.Agents.Mesh.Services;

// ============================================================
//  MeshCompilerService
//
//  Purpose:
//  - Compile + validate MeshDefinition JSON using CognitiveDslCompiler.
//  - Enforce SRA-specific allowlists for node types and constraint types.
//  - Return structured errors (do not throw to callers).
// ============================================================

public sealed record MeshCompileResult(bool Ok, MeshDefinition? Definition, IReadOnlyList<DslValidationError> Errors)
{
    public static MeshCompileResult Success(MeshDefinition def) => new(true, def, Array.Empty<DslValidationError>());
    public static MeshCompileResult Failed(params DslValidationError[] errors) => new(false, null, errors ?? Array.Empty<DslValidationError>());
}

public sealed class MeshCompilerService : IMeshCompilerService
{
    private readonly ILogger<MeshCompilerService> _logger;
    private readonly GlobalAgentYamlRegistry _roles;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public MeshCompilerService(ILogger<MeshCompilerService> logger, GlobalAgentYamlRegistry roles)
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
                Message: "mesh 内容为空。",
                Path: null));
        }

        // Configure compiler guardrails for SRA mesh orchestration.
        // NOTE:
        // - CognitiveDslCompiler enforces allowedAgentTypes. We extend it with user-defined roles discovered
        //   from ~/.aevatar/agents/*.yaml (cross-app convention).
        var options = CognitiveDslOptions.Default.With(
            allowedAgentTypes: MergeAllowedNodeTypesWithGlobalYamlRoles(),
            allowedConstraintTypes: SraMeshMappings.AllowedConstraintTypes,
            metaAgentTypeName: "meta"); // unused for SRA; keep placeholder stable

        var compiler = new CognitiveDslCompiler(options);

        string json;
        try
        {
            json = CoerceToJson(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mesh coercion failed (yaml->json).");
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.coerce_failed",
                Message: ex.Message,
                Path: null));
        }

        try
        {
            var def = compiler.Compile(json);
            return MeshCompileResult.Success(def);
        }
        catch (DslCompilationException ex)
        {
            // Structured errors from compiler.
            var errors = ex.Errors?.ToArray() ?? Array.Empty<DslValidationError>();
            if (errors.Length == 0)
            {
                var preview = json.Length <= 240 ? json : json[..240];
                errors =
                [
                    new DslValidationError("mesh.compile_failed", ex.Message, null),
                    new DslValidationError("mesh.compile_input_preview", preview, null)
                ];
            }

            return new MeshCompileResult(false, null, errors);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mesh compile failed (unexpected).");
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.compile_exception",
                Message: ex.Message,
                Path: null));
        }
    }

    private IReadOnlySet<string> MergeAllowedNodeTypesWithGlobalYamlRoles()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var x in SraMeshMappings.AllowedNodeTypes)
            set.Add(x);

        foreach (var x in _roles.GetKnownRoles())
            set.Add(x);

        // Keep placeholder stable for DSL option (even if unused by SRA).
        set.Add("meta");

        return set;
    }

    private static string CoerceToJson(string raw)
    {
        // If it already looks like JSON, keep it.
        var t = raw.TrimStart();
        if (t.StartsWith('{') || t.StartsWith('['))
            return raw;

        // Otherwise treat it as YAML and convert to JSON deterministically.
        var obj = Yaml.Deserialize<object>(raw);
        var normalized = NormalizeYaml(obj);
        var json = JsonSerializer.Serialize(normalized, Json);

        // Defensive: ensure it is valid JSON before feeding the DSL compiler.
        // If this throws, we'll surface mesh.compile_exception with the message.
        JsonDocument.Parse(json);

        return json;
    }

    private static object? NormalizeYaml(object? value)
    {
        // YamlDotNet returns Dictionary<object, object?> and List<object?>; we normalize keys to string
        // so System.Text.Json can serialize it predictably.
        switch (value)
        {
            case null:
                return null;
            case string s:
            {
                // YamlDotNet deserializing to object often yields string scalars.
                // We opportunistically coerce common scalar forms to JSON primitives,
                // but keep known DSL version values as strings.
                var t = s.Trim();
                if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

                // Mesh DSL: dsl_version is a string like "0.1".
                if (t is "0.1" or "0.2")
                    return t;

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
                return value.ToString();
        }
    }
}


