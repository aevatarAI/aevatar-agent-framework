using Aevatar.Agents.Cognitive.Primitives;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Agents.Cognitive.Engine;

// ============================================================
//  Workflow Parser - YAML → WorkflowDefinition
// ============================================================

/// <summary>
/// Workflow parser - Parse YAML files into WorkflowDefinition
/// </summary>
public class WorkflowParser
{
    private readonly IDeserializer _deserializer;
    
    public WorkflowParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }
    
    /// <summary>
    /// Parse workflow from YAML string
    /// </summary>
    public WorkflowDefinition Parse(string yaml)
    {
        var yamlDef = _deserializer.Deserialize<YamlWorkflowDefinition>(yaml);
        return ConvertToWorkflowDefinition(yamlDef);
    }
    
    /// <summary>
    /// Parse workflow from file
    /// </summary>
    public WorkflowDefinition ParseFile(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        return Parse(yaml);
    }
    
    /// <summary>
    /// Load all workflows from directory
    /// </summary>
    public IEnumerable<WorkflowDefinition> ParseDirectory(string directoryPath)
    {
        var files = Directory.GetFiles(directoryPath, "*.yaml")
            .Concat(Directory.GetFiles(directoryPath, "*.yml"));
        
        foreach (var file in files)
        {
            WorkflowDefinition? workflow = null;
            try
            {
                workflow = ParseFile(file);
            }
            catch
            {
                // Skip invalid files
            }
            
            if (workflow != null)
            {
                yield return workflow;
            }
        }
    }
    
    // ============================================================
    //  Conversion Methods
    // ============================================================
    
    private static WorkflowDefinition ConvertToWorkflowDefinition(YamlWorkflowDefinition yaml)
    {
        var defaults = NormalizeDefaults(yaml.Defaults);
        return new WorkflowDefinition
        {
            Name = yaml.Name ?? "unnamed",
            Version = string.IsNullOrWhiteSpace(yaml.Version) ? "" : yaml.Version,
            Description = yaml.Description ?? "",
            Inputs = yaml.Inputs?.Select(ConvertToInputParameter).ToList() ?? [],
            Steps = yaml.Steps?.Select(s => ConvertToStepDefinition(s, defaults)).ToList() ?? [],
            Output = yaml.Output ?? new Dictionary<string, string>()
        };
    }
    
    private static InputParameter ConvertToInputParameter(YamlInputParameter yaml)
    {
        return new InputParameter
        {
            Name = yaml.Name ?? "",
            Type = yaml.Type ?? "string",
            Required = yaml.Required,
            DefaultValue = yaml.Default
        };
    }
    
    private static StepDefinition ConvertToStepDefinition(
        YamlStepDefinition yaml,
        Dictionary<string, Dictionary<string, object?>>? defaults)
    {
        return new StepDefinition
        {
            Id = yaml.Id ?? Guid.NewGuid().ToString("N")[..8],
            Type = yaml.Type ?? "llm_call",
            Parameters = ConvertParameters(yaml, defaults),
            Store = yaml.Store,
            Condition = yaml.Condition,
            IfTrue = yaml.IfTrue?.Select(s => ConvertToStepDefinition(s, defaults)).ToList(),
            IfFalse = yaml.IfFalse?.Select(s => ConvertToStepDefinition(s, defaults)).ToList(),
            Generator = yaml.Generator != null ? ConvertToStepDefinition(yaml.Generator, defaults) : null,
            ForEach = yaml.ForEach,
            Step = yaml.Step != null ? ConvertToStepDefinition(yaml.Step, defaults) : null,
            Reduce = yaml.Reduce,
            MaxConcurrency = yaml.MaxConcurrency,
            Workflow = yaml.Workflow,
            Params = yaml.Params != null ? (Dictionary<string, object?>)NormalizeYamlValue(yaml.Params)! : null,
            MaxDepth = yaml.MaxDepth
        };
    }
    
    private static Dictionary<string, object?> ConvertParameters(
        YamlStepDefinition yaml,
        Dictionary<string, Dictionary<string, object?>>? defaults)
    {
        // Start from workflow-level defaults for this step type, then override with step-local fields.
        var parameters = new Dictionary<string, object?>();

        var stepType = (yaml.Type ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(stepType) &&
            defaults != null &&
            defaults.TryGetValue(stepType, out var defaultParams))
        {
            foreach (var (k, v) in defaultParams)
            {
                parameters[k] = v;
            }
        }
        
        // Add type-specific parameters
        if (!string.IsNullOrEmpty(yaml.Prompt))
            parameters["prompt"] = yaml.Prompt;
        
        if (!string.IsNullOrEmpty(yaml.System))
            parameters["system"] = yaml.System;

        if (!string.IsNullOrEmpty(yaml.Agent))
            parameters["agent"] = yaml.Agent;
        else if (!string.IsNullOrEmpty(yaml.Role))
            parameters["agent"] = yaml.Role;
        
        if (!string.IsNullOrEmpty(yaml.Output))
            parameters["output"] = yaml.Output;
        
        if (yaml.K != null)
            parameters["k"] = yaml.K;
        
        if (yaml.MaxRounds != null)
            parameters["max_rounds"] = yaml.MaxRounds;
        
        if (yaml.Similarity != null)
            parameters["similarity"] = yaml.Similarity;

        // red-flag / parsing / length guardrails (step-level overrides)
        if (yaml.RedFlag != null)
            parameters["red_flag"] = NormalizeYamlValue(yaml.RedFlag);

        if (yaml.MaxRedFlags != null)
            parameters["max_red_flags"] = yaml.MaxRedFlags;

        if (yaml.MaxLength != null)
            parameters["max_length"] = yaml.MaxLength;

        if (yaml.StrictParse != null)
            parameters["strict_parse"] = yaml.StrictParse;

        // timeouts (step-level overrides)
        if (yaml.TimeoutSeconds != null)
            parameters["timeout_seconds"] = yaml.TimeoutSeconds;

        if (yaml.IdleTimeoutSeconds != null)
            parameters["idle_timeout_seconds"] = yaml.IdleTimeoutSeconds;

        // fan_out: include failures into results list (opt-in)
        if (yaml.IncludeFailures != null)
            parameters["include_failures"] = yaml.IncludeFailures;
        
        if (yaml.Variables != null)
            parameters["variables"] = yaml.Variables;
        
        if (yaml.Steps != null)
            parameters["steps"] = yaml.Steps.Select(s => ConvertToStepDefinition(s, defaults)).ToList();

        // assign
        if (!string.IsNullOrEmpty(yaml.From))
            parameters["from"] = yaml.From;

        // transform (deterministic, token-free)
        if (yaml.Ops != null)
            parameters["ops"] = NormalizeTransformOps(yaml.Ops);

        // retrieve_facts (deterministic retrieval)
        if (!string.IsNullOrEmpty(yaml.Query))
            parameters["query"] = yaml.Query;
        if (!string.IsNullOrEmpty(yaml.Source))
            parameters["source"] = yaml.Source;
        if (!string.IsNullOrEmpty(yaml.TextField))
            parameters["text_field"] = yaml.TextField;
        if (!string.IsNullOrEmpty(yaml.IdField))
            parameters["id_field"] = yaml.IdField;
        if (yaml.TopK != null)
            parameters["top_k"] = yaml.TopK;
        if (!string.IsNullOrEmpty(yaml.Mode))
            parameters["mode"] = yaml.Mode;

        // --------------------------------------------------------
        // ralph-loop primitives (workspace + verifier)
        // --------------------------------------------------------

        // workspace_read_file
        if (!string.IsNullOrEmpty(yaml.Path))
            parameters["path"] = yaml.Path;
        if (yaml.MaxChars != null)
            parameters["max_chars"] = NormalizeYamlValue(yaml.MaxChars);

        // workspace_code_search
        if (!string.IsNullOrEmpty(yaml.Pattern))
            parameters["pattern"] = yaml.Pattern;
        if (!string.IsNullOrEmpty(yaml.Glob))
            parameters["glob"] = yaml.Glob;
        if (!string.IsNullOrEmpty(yaml.FileType))
            parameters["file_type"] = yaml.FileType;
        if (yaml.MaxResults != null)
            parameters["max_results"] = NormalizeYamlValue(yaml.MaxResults);
        if (yaml.ContextLines != null)
            parameters["context_lines"] = NormalizeYamlValue(yaml.ContextLines);

        // workspace_apply_patch
        if (yaml.Patch != null)
            parameters["patch"] = NormalizeYamlValue(yaml.Patch);
        if (yaml.Patches != null)
            parameters["patches"] = NormalizeYamlValue(yaml.Patches);

        // sandbox_command
        if (!string.IsNullOrEmpty(yaml.Command))
            parameters["command"] = yaml.Command;
        if (yaml.Args != null)
            parameters["args"] = NormalizeYamlValue(yaml.Args);
        if (!string.IsNullOrEmpty(yaml.WorkingDir))
            parameters["working_dir"] = yaml.WorkingDir;
        if (yaml.TimeoutMs != null)
            parameters["timeout_ms"] = NormalizeYamlValue(yaml.TimeoutMs);
        if (yaml.MaxOutputChars != null)
            parameters["max_output_chars"] = NormalizeYamlValue(yaml.MaxOutputChars);
        
        return parameters;
    }

    // ============================================================
    //  YAML normalization helpers
    //
    //  WHY:
    //  - YamlDotNet will often deserialize nested mappings into Dictionary<object, object>.
    //  - Our runtime code expects Dictionary<string, object?> (e.g., red_flag config, transform.where).
    //  - Normalizing here makes DSL config actually effective (no silent fallbacks).
    // ============================================================

    private static Dictionary<string, Dictionary<string, object?>>? NormalizeDefaults(
        Dictionary<string, Dictionary<string, object?>>? defaults)
    {
        if (defaults == null) return null;

        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var (type, dict) in defaults)
        {
            result[type] = (Dictionary<string, object?>)NormalizeYamlValue(dict)!;
        }
        return result;
    }

    private static List<Dictionary<string, object?>> NormalizeTransformOps(List<Dictionary<string, object?>> ops)
    {
        var outOps = new List<Dictionary<string, object?>>(ops.Count);
        foreach (var op in ops)
        {
            outOps.Add((Dictionary<string, object?>)NormalizeYamlValue(op)!);
        }
        return outOps;
    }

    private static object? NormalizeYamlValue(object? value)
    {
        if (value == null) return null;

        // Common case: already normalized.
        if (value is Dictionary<string, object?> ds)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in ds)
                dict[k] = NormalizeYamlValue(v);
            return dict;
        }

        // YamlDotNet nested mapping often lands here.
        if (value is Dictionary<object, object> dobj)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in dobj)
                dict[k?.ToString() ?? ""] = NormalizeYamlValue(v);
            return dict;
        }

        if (value is System.Collections.IDictionary nd)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry e in nd)
                dict[e.Key?.ToString() ?? ""] = NormalizeYamlValue(e.Value);
            return dict;
        }

        if (value is List<object> listObj)
        {
            return listObj.Select(NormalizeYamlValue).ToList();
        }

        if (value is System.Collections.IList list)
        {
            var outList = new List<object?>(list.Count);
            foreach (var it in list)
                outList.Add(NormalizeYamlValue(it));
            return outList;
        }

        return value;
    }
}

// ============================================================
//  YAML Data Models
// ============================================================

internal class YamlWorkflowDefinition
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public List<YamlInputParameter>? Inputs { get; set; }
    public List<YamlStepDefinition>? Steps { get; set; }
    public Dictionary<string, string>? Output { get; set; }

    // Workflow-level defaults (e.g., maker.yaml)
    // defaults:
    //   vote: { k: 3, max_rounds: 10, red_flag: { ... } }
    //   llm_call: { max_length: 102400, strict_parse: true }
    public Dictionary<string, Dictionary<string, object?>>? Defaults { get; set; }
}

internal class YamlInputParameter
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool Required { get; set; }
    public object? Default { get; set; }
}

internal class YamlStepDefinition
{
    // Basic fields
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Store { get; set; }
    
    // llm_call fields
    public string? Prompt { get; set; }
    public string? System { get; set; }
    public string? Output { get; set; }
    public string? Agent { get; set; }
    public string? Role { get; set; }

    // common guardrails (may appear on llm_call / vote / etc.)
    public object? MaxLength { get; set; }              // max_length
    public object? StrictParse { get; set; }            // strict_parse
    public object? TimeoutSeconds { get; set; }         // timeout_seconds
    public object? IdleTimeoutSeconds { get; set; }     // idle_timeout_seconds
    
    // conditional fields
    public string? Condition { get; set; }
    public List<YamlStepDefinition>? IfTrue { get; set; }
    public List<YamlStepDefinition>? IfFalse { get; set; }
    
    // vote fields (supports template variables like "{{k}}")
    public object? K { get; set; }
    public object? MaxRounds { get; set; }
    public object? Similarity { get; set; }
    public YamlStepDefinition? Generator { get; set; }

    // vote red-flagging
    public object? RedFlag { get; set; }                // red_flag (bool|string|mapping)
    public object? MaxRedFlags { get; set; }            // max_red_flags
    
    // fan_out fields (supports template variables)
    public string? ForEach { get; set; }
    public YamlStepDefinition? Step { get; set; }
    public string? Reduce { get; set; }
    public object? MaxConcurrency { get; set; }

    // fan_out: return failures as items (opt-in)
    public bool? IncludeFailures { get; set; }          // include_failures
    
    // workflow_call fields (supports template variables)
    public string? Workflow { get; set; }
    public Dictionary<string, object?>? Params { get; set; }
    public object? MaxDepth { get; set; }
    
    // checkpoint fields
    public List<string>? Variables { get; set; }
    
    // assign fields
    // - from: "recursive_output.state" (supports dotted path)
    public string? From { get; set; }

    // parallel fields
    public List<YamlStepDefinition>? Steps { get; set; }

    // transform fields
    // - ops: [{ op: "...", ... }]
    public List<Dictionary<string, object?>>? Ops { get; set; }

    // retrieve_facts fields
    public string? Query { get; set; }       // template string
    public string? Source { get; set; }      // dotted path to list
    public string? TextField { get; set; }   // default: "statement"
    public string? IdField { get; set; }     // default: "id"
    public object? TopK { get; set; }        // int or "{{var}}"
    public string? Mode { get; set; }        // "lexical" (default) | "embedding" (future)

    // ------------------------------------------------------------
    //  ralph-loop primitives (deterministic workspace + verifier)
    //
    //  NOTE:
    //  - We keep numeric/budget fields as object? to allow template variables like "{{max_chars}}".
    //  - All of these are converted into StepDefinition.Parameters with snake_case keys.
    // ------------------------------------------------------------

    // workspace_read_file
    public string? Path { get; set; }        // path
    public object? MaxChars { get; set; }    // max_chars

    // workspace_code_search
    public string? Pattern { get; set; }     // pattern
    public string? Glob { get; set; }        // glob
    public string? FileType { get; set; }    // file_type
    public object? MaxResults { get; set; }  // max_results
    public object? ContextLines { get; set; } // context_lines

    // workspace_apply_patch
    public object? Patch { get; set; }       // patch (string or structured)
    public object? Patches { get; set; }     // patches (array or structured)

    // sandbox_command
    public string? Command { get; set; }     // command
    public object? Args { get; set; }        // args (array or template)
    public string? WorkingDir { get; set; }  // working_dir
    public object? TimeoutMs { get; set; }   // timeout_ms
    public object? MaxOutputChars { get; set; } // max_output_chars
}

