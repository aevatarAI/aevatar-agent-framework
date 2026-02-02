using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  WorkflowCoordinatorAgent - Parameters / Red-Flag / Parsing
//
//  WHY:
//  - These methods are shared by multiple paths like vote / fan_out / llm_call.
//  - Centralized management to avoid "same configuration semantics" scattered causing drift.
// ============================================================

public partial class WorkflowCoordinatorAgent
{
    /// <summary>
    /// Parse LLM returned content based on output type
    /// </summary>
    private object? ParseOutput(string content, string outputType)
    {
        // Prefer the shared factory instance (avoid unnecessary allocations).
        var parser = _parserFactory.Create(outputType);
        try
        {
            return parser.Parse(content);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ParseOutput failed for type {Type}", outputType);
            return null;
        }
    }

    /// <summary>
    /// Resolve step-level Red-Flag strategy
    /// DSL syntax:
    ///   red_flag: english    # Use English strategy
    ///   red_flag: chinese    # Use Chinese strategy
    ///   red_flag: code       # Use code strategy
    ///   red_flag: false      # Disable
    ///   red_flag: true       # Use Coordinator default configuration
    ///   (not written)        # Use Coordinator default configuration
    /// </summary>
    private IRedFlagStrategy? ResolveRedFlagStrategy(Dictionary<string, object?> parameters)
    {
        var redFlagConfig = parameters.GetValueOrDefault("red_flag");

        return redFlagConfig switch
        {
            // Explicitly disabled
            false or "false" or "none" or "disabled" => null,

            // Explicitly enabled (use default configuration)
            true or "true" or "default" => _redFlagStrategy,

            // Specify strategy name
            "english" => new DefaultEnglishRedFlagStrategy(),
            "chinese" => new ChineseRedFlagStrategy(),
            "code" => new CodeAwareRedFlagStrategy(),

            // Nested configuration object
            Dictionary<string, object?> config => ResolveRedFlagFromConfig(config),

            // Not configured: use Coordinator default
            null => _redFlagStrategy,

            // Other: try parsing as strategy name
            string name => ResolveRedFlagByName(name),

            _ => _redFlagStrategy
        };
    }

    private IRedFlagStrategy? ResolveRedFlagFromConfig(Dictionary<string, object?> config)
    {
        var enabled = config.GetValueOrDefault("enabled");
        if (enabled is false or "false")
        {
            return null;
        }

        var strategyName = config.GetValueOrDefault("strategy")?.ToString() ?? "english";
        var options = new RedFlagOptions
        {
            MinContentLength = config.TryGetValue("min_length", out var min) && min != null
                ? Convert.ToInt32(min)
                : 10,
            MaxContentLength = config.TryGetValue("max_length", out var max) && max != null
                ? Convert.ToInt32(max)
                : 8000,
            EnableRefusalDetection = config.GetValueOrDefault("detect_refusal") is not (false or "false"),
            EnableDegenerationDetection = config.GetValueOrDefault("detect_degeneration") is not (false or "false"),
            EnableLengthValidation = config.GetValueOrDefault("validate_length") is not (false or "false")
        };

        return strategyName.ToLowerInvariant() switch
        {
            "english" => new DefaultEnglishRedFlagStrategy(options),
            "chinese" => new ChineseRedFlagStrategy(options),
            "code" => new CodeAwareRedFlagStrategy(options),
            "none" or "disabled" => null,
            _ => new DefaultEnglishRedFlagStrategy(options)
        };
    }

    private IRedFlagStrategy? ResolveRedFlagByName(string name) => name.ToLowerInvariant() switch
    {
        "english" => new DefaultEnglishRedFlagStrategy(),
        "chinese" => new ChineseRedFlagStrategy(),
        "code" => new CodeAwareRedFlagStrategy(),
        "none" or "disabled" or "false" => null,
        _ => _redFlagStrategy
    };

    // ============================================================
    //  Parameter parsing helper methods
    // ============================================================

    private int ResolveIntParameter(Dictionary<string, object?> parameters, string key, int defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            string s => ConvertToInt(_templateEngine.Evaluate(s, _workflowVariables), defaultValue),
            _ => defaultValue
        };
    }

    private float ResolveFloatParameter(Dictionary<string, object?> parameters, string key, float defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);

        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            string s when float.TryParse(s, out var parsed) => parsed,
            string s => ConvertToFloat(_templateEngine.Evaluate(s, _workflowVariables), defaultValue),
            _ => defaultValue
        };
    }

    /// <summary>
    /// Safely convert to int, handle object unboxing
    /// </summary>
    private static int ConvertToInt(object? value, int defaultValue) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    private bool ResolveBoolParameter(Dictionary<string, object?> parameters, string key, bool defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);

        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s when bool.TryParse(s, out var parsed) => parsed,
            string s => ConvertToBool(_templateEngine.Evaluate(s, _workflowVariables), defaultValue),
            _ => defaultValue
        };
    }

    private static bool ConvertToBool(object? value, bool defaultValue) => value switch
    {
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        double d => Math.Abs(d) > double.Epsilon,
        float f => Math.Abs(f) > float.Epsilon,
        decimal m => m != 0,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    /// <summary>
    /// Safely convert to float, handle object unboxing
    /// </summary>
    private static float ConvertToFloat(object? value, float defaultValue) => value switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        long l => l,
        decimal m => (float)m,
        string s when float.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };
}

