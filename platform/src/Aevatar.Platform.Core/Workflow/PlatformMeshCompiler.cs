using System.Text.Json;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.CognitiveMesh.Dsl;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;
using Aevatar.CognitiveMesh.Dsl.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Platform.Core.Workflow;

// ============================================================
//  PlatformMeshCompiler
//
//  Purpose:
//  - Compile Mesh DSL from JSON or YAML (deterministic YAML->JSON).
//  - Merge allowed agent types with global roles (~/.aevatar/agents/*.yaml).
//  - Return structured errors (do not throw to UI).
// ============================================================
public sealed record PlatformMeshCompileResult(bool Ok, MeshDefinition? Definition, IReadOnlyList<DslValidationError> Errors)
{
    public static PlatformMeshCompileResult Success(MeshDefinition def) => new(true, def, Array.Empty<DslValidationError>());
    public static PlatformMeshCompileResult Failed(params DslValidationError[] errors)
        => new(false, null, errors ?? Array.Empty<DslValidationError>());
}

public sealed class PlatformMeshCompiler
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private readonly GlobalAgentYamlRegistry _roles;
    private readonly CognitiveDslOptions _baseOptions;
    private readonly string? _localAgentRoot;
    private readonly string? _configAgentsDir;

    public PlatformMeshCompiler(
        GlobalAgentYamlRegistry? roles = null,
        CognitiveDslOptions? baseOptions = null,
        string? localAgentRoot = null,
        string? configAgentsDir = null)
    {
        _roles = roles ?? new GlobalAgentYamlRegistry(NullLogger<GlobalAgentYamlRegistry>.Instance);
        _baseOptions = baseOptions ?? CognitiveDslOptions.Default;
        _localAgentRoot = string.IsNullOrWhiteSpace(localAgentRoot) ? null : localAgentRoot.Trim();
        _configAgentsDir = string.IsNullOrWhiteSpace(configAgentsDir) ? null : configAgentsDir.Trim();
    }

    public PlatformMeshCompileResult Compile(string raw)
    {
        raw = (raw ?? string.Empty).Replace("\r", "").Trim();
        if (raw.Length == 0)
        {
            return PlatformMeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.empty",
                Message: "mesh 内容为空。",
                Path: null));
        }

        string json;
        try
        {
            json = CoerceToJson(raw);
        }
        catch (Exception ex)
        {
            return PlatformMeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.coerce_failed",
                Message: ex.Message,
                Path: null));
        }

        var options = BuildOptionsWithRoles();
        var compiler = new CognitiveDslCompiler(options);

        try
        {
            var def = compiler.Compile(json);
            return PlatformMeshCompileResult.Success(def);
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

            return PlatformMeshCompileResult.Failed(errors);
        }
        catch (Exception ex)
        {
            return PlatformMeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.compile_exception",
                Message: ex.Message,
                Path: null));
        }
    }

    private CognitiveDslOptions BuildOptionsWithRoles()
    {
        var mergedAgents = new HashSet<string>(_baseOptions.AllowedAgentTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var role in _roles.GetKnownRoles())
            mergedAgents.Add(role);
        foreach (var role in GetLocalRoles(_localAgentRoot))
            mergedAgents.Add(role);
        foreach (var role in GetRolesFromDirectory(_configAgentsDir))
            mergedAgents.Add(role);

        return _baseOptions.With(allowedAgentTypes: mergedAgents);
    }

    private static IReadOnlyCollection<string> GetLocalRoles(string? root)
    {
        try
        {
            var baseDir = string.IsNullOrWhiteSpace(root) ? Directory.GetCurrentDirectory() : root;
            if (string.IsNullOrWhiteSpace(baseDir))
                return Array.Empty<string>();

            var dir = Path.Combine(baseDir, "aevatar", "agents");
            return GetRolesFromDirectory(dir);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyCollection<string> GetRolesFromDirectory(string? dir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return Array.Empty<string>();

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".yml", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = Path.GetFileNameWithoutExtension(file);
                var key = GlobalAgentYamlRegistry.NormalizeRoleKey(name);
                if (key.Length > 0)
                    roles.Add(key);
            }

            return roles;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string CoerceToJson(string raw)
    {
        var t = raw.TrimStart();
        if (t.StartsWith('{') || t.StartsWith('['))
            return raw;

        // Otherwise treat it as YAML and convert to JSON deterministically.
        var obj = Yaml.Deserialize<object>(raw);
        var normalized = NormalizeYaml(obj);
        var json = JsonSerializer.Serialize(normalized);

        // Defensive: ensure it is valid JSON before feeding compiler.
        JsonDocument.Parse(json);
        return json;
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


