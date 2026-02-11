using System.Collections.Concurrent;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Resolves Protobuf event types using reflection and caching.
/// Supports cross-assembly lookup for multi-module deployments.
/// </summary>
public class ProtobufEventTypeResolver : IEventTypeResolver
{
    private readonly ILogger<ProtobufEventTypeResolver>? _logger;
    
    // Key: Protobuf full type name (e.g., "aevatar.agents.banking.MoneyDeposited")
    // Value: Cached parser and metadata
    private readonly ConcurrentDictionary<string, EventTypeInfo> _typeCache = new();

    public ProtobufEventTypeResolver(ILogger<ProtobufEventTypeResolver>? logger = null)
    {
        _logger = logger;
    }

    public EventTypeInfo? Resolve(string typeUrl, Assembly searchAssembly)
    {
        var fullTypeName = ExtractFullTypeName(typeUrl);
        var simpleTypeName = ExtractSimpleTypeName(fullTypeName);

        // Fast path: cache hit by full protobuf name
        if (_typeCache.TryGetValue(fullTypeName, out var info))
        {
            return info;
        }

        // Slow path: build and cache
        info = BuildTypeCache(fullTypeName, simpleTypeName, searchAssembly);
        if (info != null)
        {
            _typeCache[fullTypeName] = info;
            _logger?.LogInformation(
                "Type {TypeName} cached. Total cached types: {Count}",
                fullTypeName, _typeCache.Count);
        }

        return info;
    }

    private EventTypeInfo? BuildTypeCache(
        string fullTypeName,
        string simpleTypeName,
        Assembly searchAssembly)
    {
        try
        {
            // 1) Try searchAssembly first (fast path for single-assembly scenarios)
            var matchingType = FindMessageTypeByDescriptorFullName(searchAssembly, fullTypeName)
                               ?? FindMessageTypeBySimpleName(searchAssembly, simpleTypeName);

            // 2) Fallback: scan all loaded assemblies (cross-module events)
            if (matchingType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()
                             .Where(a => !a.IsDynamic)
                             .OrderBy(a => a.FullName, StringComparer.Ordinal))
                {
                    matchingType = FindMessageTypeByDescriptorFullName(asm, fullTypeName)
                                   ?? FindMessageTypeBySimpleName(asm, simpleTypeName);
                    if (matchingType != null)
                        break;
                }
            }

            if (matchingType == null)
            {
                _logger?.LogWarning(
                    "Type {TypeName} not found (fullName: {FullTypeName}) starting from assembly {Assembly}",
                    simpleTypeName, fullTypeName, searchAssembly.FullName);
                return null;
            }

            var parser = matchingType
                .GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as MessageParser;

            if (parser == null)
            {
                _logger?.LogWarning(
                    "Parser property not found for type {TypeName}",
                    matchingType.FullName);
                return null;
            }

            _logger?.LogDebug(
                "Built type cache for {TypeName} (type: {FullName})",
                simpleTypeName, matchingType.FullName);

            return new EventTypeInfo(matchingType, parser);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error building type cache for {TypeName}", fullTypeName);
            return null;
        }
    }

    /// <summary>
    /// Extract full protobuf type name from typeUrl.
    /// e.g., "type.googleapis.com/aevatar.agents.banking.MoneyDeposited"
    ///     -> "aevatar.agents.banking.MoneyDeposited"
    /// </summary>
    private static string ExtractFullTypeName(string typeUrl)
    {
        if (string.IsNullOrWhiteSpace(typeUrl))
            return string.Empty;
        var idx = typeUrl.LastIndexOf('/');
        return idx >= 0 && idx < typeUrl.Length - 1
            ? typeUrl[(idx + 1)..]
            : typeUrl.Trim();
    }

    /// <summary>
    /// Extract simple C# type name from protobuf full name.
    /// e.g., "aevatar.agents.banking.MoneyDeposited" -> "MoneyDeposited"
    /// </summary>
    private static string ExtractSimpleTypeName(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
            return string.Empty;
        return fullTypeName.Contains('.')
            ? fullTypeName[(fullTypeName.LastIndexOf('.') + 1)..]
            : fullTypeName;
    }

    /// <summary>
    /// Exact match by Protobuf Descriptor.FullName (handles package-qualified names).
    /// </summary>
    private static Type? FindMessageTypeByDescriptorFullName(Assembly assembly, string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
            return null;

        try
        {
            foreach (var t in assembly.GetTypes())
            {
                if (!typeof(IMessage).IsAssignableFrom(t)) continue;

                var descObj = t.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                if (descObj is not MessageDescriptor desc) continue;
                if (string.Equals(desc.FullName, fullTypeName, StringComparison.Ordinal))
                    return t;
            }
        }
        catch
        {
            // Best-effort: some assemblies may throw on GetTypes()
        }

        return null;
    }

    /// <summary>
    /// Fallback match by simple C# class name.
    /// </summary>
    private static Type? FindMessageTypeBySimpleName(Assembly assembly, string simpleTypeName)
    {
        if (string.IsNullOrWhiteSpace(simpleTypeName))
            return null;

        try
        {
            return assembly.GetTypes()
                .FirstOrDefault(t => t.Name == simpleTypeName && typeof(IMessage).IsAssignableFrom(t));
        }
        catch
        {
            return null;
        }
    }
    
    public void ClearCache()
    {
        _typeCache.Clear();
    }
}
