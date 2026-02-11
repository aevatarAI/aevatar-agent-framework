using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace Aevatar.Agents.Core.Rpc;

/// <summary>
/// Shared RPC invocation logic for all runtime implementations.
/// Handles method discovery, argument deserialization, invocation, and result serialization.
/// </summary>
public static class RpcInvoker
{

    /// <summary>
    /// Invoke RPC method on Agent
    /// </summary>
    public static async Task<byte[]> InvokeAsync(IGAgent agent, byte[] requestBytes, ILogger? logger = null, CancellationToken ct = default)
    {
        var request = RpcRequest.Parser.ParseFrom(requestBytes);
        var response = new RpcResponse
        {
            CorrelationId = request.CorrelationId,
            Success = false
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var agentId = agent?.Id ?? "unknown";
        var agentType = agent?.GetType().Name ?? "unknown";
        
        try
        {
            if (agent == null)
                throw new InvalidOperationException("Agent not initialized");

            logger?.LogDebug("[PERF][RPC] Starting {Method} on {AgentId} ({AgentType})", 
                request.MethodName, agentId, agentType);

            // Pass argument count to select correct method overload
            var method = GetCachedMethod(agent.GetType(), request.MethodName, request.Args.Count);
            var args = DeserializeArgs(request.Args, method.GetParameters(), ct);
            var result = method.Invoke(agent, args);

            // Handle async methods
            if (result is Task task)
            {
                await task;
                result = GetTaskResult(task);
            }

            // Always pack result (even if null, it will be packed as Empty)
            response.Result = ProtobufPacker.Pack(result);
            response.Success = true;
            
            stopwatch.Stop();
            // Log warning if RPC takes more than 1 second
            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                logger?.LogWarning("[PERF][RPC] ⚠️ SLOW: {Method} on {AgentId} ({AgentType}) took {Duration}ms", 
                    request.MethodName, agentId, agentType, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger?.LogDebug("[PERF][RPC] Completed {Method} on {AgentId}: {Duration}ms", 
                    request.MethodName, agentId, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var innerEx = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            response.Error = new RpcError
            {
                ErrorType = innerEx.GetType().FullName ?? innerEx.GetType().Name,
                Message = innerEx.Message,
                StackTrace = innerEx.StackTrace ?? string.Empty
            };
            logger?.LogError(innerEx, "[PERF][RPC] ❌ {Method} on {AgentId} failed after {Duration}ms: {Error}", 
                request.MethodName, agentId, stopwatch.ElapsedMilliseconds, innerEx.Message);
        }

        return response.ToByteArray();
    }

    private static readonly ConcurrentDictionary<(System.Type, string, int), MethodInfo> MethodCacheWithArgs = new();

    private static MethodInfo GetCachedMethod(System.Type agentType, string methodName, int argCount)
    {
        return MethodCacheWithArgs.GetOrAdd((agentType, methodName, argCount), k =>
        {
            // Find all methods with the given name
            var methods = k.Item1.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                .Where(m => m.Name.Equals(k.Item2, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (methods.Count == 0)
            {
                throw new InvalidOperationException($"Method '{k.Item2}' not found on type '{k.Item1.Name}'");
            }

            // Select method based on argument count.
            // CancellationToken parameters are injected by the framework (not sent by client),
            // so exclude them from the effective parameter count when matching.
            MethodInfo? method = null;
            if (methods.Count == 1)
            {
                method = methods[0];
            }
            else
            {
                // Multiple overloads - match by effective param count (excluding CancellationToken)
                method = methods.FirstOrDefault(m =>
                    CountEffectiveParams(m) == k.Item3);
                if (method == null)
                {
                    // Fallback: optional parameters
                    method = methods
                        .Where(m =>
                        {
                            var nonCt = m.GetParameters()
                                .Where(p => p.ParameterType != typeof(CancellationToken));
                            var requiredCount = nonCt.Count(p => !p.HasDefaultValue);
                            var totalCount = nonCt.Count();
                            return requiredCount <= k.Item3 && totalCount >= k.Item3;
                        })
                        .FirstOrDefault();
                }
            }

            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Method '{k.Item2}' with {k.Item3} arguments not found on type '{k.Item1.Name}'. " +
                    $"Available overloads have {string.Join(", ", methods.Select(m => m.GetParameters().Length))} parameters.");
            }

            // Validate method is defined in an interface
            var paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var isInInterface = k.Item1.GetInterfaces().Any(iface =>
                iface.GetMethod(method.Name, paramTypes) is { } m && m.ReturnType == method.ReturnType);

            if (!isInInterface)
            {
                throw new InvalidOperationException(
                    $"Method '{method.Name}' is not defined in any interface. " +
                    $"Only methods defined in interfaces can be called via RPC.");
            }

            return method;
        });
    }

    /// <summary>
    /// Count parameters excluding CancellationToken (which is framework-injected, not client-supplied).
    /// </summary>
    private static int CountEffectiveParams(MethodInfo method)
    {
        return method.GetParameters().Count(p => p.ParameterType != typeof(CancellationToken));
    }

    /// <summary>
    /// Deserialize protobuf args into method parameters.
    /// CancellationToken parameters are automatically injected from the provided <paramref name="ct"/>,
    /// since they cannot be serialized over the wire.
    /// </summary>
    private static object?[] DeserializeArgs(
        Google.Protobuf.Collections.RepeatedField<Any> protoArgs,
        ParameterInfo[] paramInfos,
        CancellationToken ct = default)
    {
        var args = new object?[paramInfos.Length];
        var protoIdx = 0;

        for (var i = 0; i < paramInfos.Length; i++)
        {
            var p = paramInfos[i];

            if (p.ParameterType == typeof(CancellationToken))
            {
                // Framework-injected: propagate the caller's CancellationToken
                args[i] = ct;
            }
            else if (protoIdx < protoArgs.Count)
            {
                args[i] = ProtobufPacker.Unpack(protoArgs[protoIdx], p.ParameterType);
                protoIdx++;
            }
            else
            {
                args[i] = p.HasDefaultValue ? p.DefaultValue : null;
            }
        }

        return args;
    }

    private static object? GetTaskResult(Task task)
    {
        return task.GetType().IsGenericType
            ? task.GetType().GetProperty("Result")?.GetValue(task)
            : null;
    }
}

