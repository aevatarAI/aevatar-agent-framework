using System.Reflection;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.Rpc;
using Google.Protobuf;

namespace Aevatar.Agents.Abstractions.Extensions;

/// <summary>
/// RPC extensions for IGAgentActor - unified API for both type-safe and dynamic calls
/// </summary>
public static class RpcExtensions
{
    #region Type-Safe API (Recommended)

    /// <summary>
    /// Create a type-safe proxy for calling agent methods via interface.
    /// Automatically selects optimal execution path:
    /// - Local Runtime: Direct method call (zero overhead)
    /// - Remote Runtime: RPC via Protobuf serialization
    /// </summary>
    /// <example>
    /// var bankAgent = actor.As&lt;IBankAccountAgent&gt;();
    /// await bankAgent.CreateAccountAsync("User", 1000m);
    /// </example>
    public static TInterface As<TInterface>(this IGAgentActor actor) where TInterface : class
    {
        if (!typeof(TInterface).IsInterface)
            throw new ArgumentException($"{typeof(TInterface).Name} must be an interface");

        return RpcProxy<TInterface>.Create(actor);
    }

    #endregion

    #region Dynamic API (For special cases)

    /// <summary>
    /// Invoke RPC method by name with typed result (use As&lt;T&gt;() when possible)
    /// </summary>
    public static async Task<TResult> InvokeAsync<TResult>(
        this IGAgentActor actor,
        string methodName,
        params object?[] args)
    {
        var response = await InvokeRpcCoreAsync(actor, methodName, args);
        return ProtobufPacker.Unpack<TResult>(response.Result);
    }

    /// <summary>
    /// Invoke RPC method by name without return value (use As&lt;T&gt;() when possible)
    /// </summary>
    public static async Task InvokeAsync(
        this IGAgentActor actor,
        string methodName,
        params object?[] args)
    {
        await InvokeRpcCoreAsync(actor, methodName, args);
    }

    private static async Task<RpcResponse> InvokeRpcCoreAsync(
        IGAgentActor actor,
        string methodName,
        object?[] args)
    {
        var request = new RpcRequest
        {
            MethodName = methodName,
            CorrelationId = Guid.NewGuid().ToString()
        };

        foreach (var arg in args)
            request.Args.Add(ProtobufPacker.Pack(arg));

        var responseBytes = await actor.InvokeRpcAsync(request.ToByteArray());
        var response = RpcResponse.Parser.ParseFrom(responseBytes);

        if (!response.Success)
        {
            throw new InvalidOperationException(
                $"RPC call '{methodName}' failed: {response.Error?.Message ?? "Unknown error"}");
        }

        return response;
    }

    #endregion
}

/// <summary>
/// Dynamic proxy for type-safe RPC calls through interface.
/// Auto-detects runtime and method source:
/// - Local Runtime: Direct method call (zero overhead)
/// - Remote Runtime + IGAgentActor method: Delegate to actor (no RPC serialization)
/// - Remote Runtime + Business method: RPC via Protobuf
/// </summary>
internal class RpcProxy<TInterface> : DispatchProxy where TInterface : class
{
    // Static cache: IGAgentActor methods (initialized once per AppDomain)
    private static readonly Dictionary<string, MethodInfo> ActorMethods =
        typeof(IGAgentActor)
            .GetInterfaces()
            .Prepend(typeof(IGAgentActor))
            .SelectMany(i => i.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .DistinctBy(m => GetMethodKey(m))
            .ToDictionary(m => GetMethodKey(m), m => m);

    private static string GetMethodKey(MethodInfo m) =>
        $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName))})";

    private IGAgentActor _actor = null!;
    private TInterface? _directAgent;
    private bool _isLocalRuntime;

    public static TInterface Create(IGAgentActor actor)
    {
        var proxy = Create<TInterface, RpcProxy<TInterface>>() as RpcProxy<TInterface>;
        proxy!._actor = actor;

        // Try direct agent reference (Local runtime)
        try
        {
            if (actor.GetAgent() is TInterface typedAgent)
            {
                proxy._directAgent = typedAgent;
                proxy._isLocalRuntime = true;
            }
        }
        catch (NotSupportedException)
        {
            // Remote runtime - use RPC or delegate
            proxy._isLocalRuntime = false;
        }

        return (proxy as TInterface)!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
            throw new ArgumentNullException(nameof(targetMethod));

        // 1. Local runtime: Direct call to Agent (zero overhead)
        if (_isLocalRuntime && _directAgent != null)
            return targetMethod.Invoke(_directAgent, args);

        // 2. Remote runtime: Check if IGAgentActor has this method
        var methodKey = GetMethodKey(targetMethod);
        if (ActorMethods.TryGetValue(methodKey, out var actorMethod))
        {
            // Delegate to actor directly (no RPC serialization)
            return actorMethod.Invoke(_actor, args);
        }

        // 3. Business method: RPC via Protobuf
        return InvokeViaRpc(targetMethod, args);
    }

    private object? InvokeViaRpc(MethodInfo targetMethod, object?[]? args)
    {
        var request = new RpcRequest
        {
            MethodName = targetMethod.Name,
            CorrelationId = Guid.NewGuid().ToString()
        };

        foreach (var arg in args ?? [])
            request.Args.Add(ProtobufPacker.Pack(arg));

        // Check if method is marked as read-only (safe for concurrent execution):
        // - [ReadOnlyRpc] - our framework attribute (recommended)
        // - [ReadOnly] - Orleans attribute (for compatibility with existing code)
        var isReadOnlyRpc = targetMethod.GetCustomAttributes(true)
            .Any(attr => attr.GetType().Name == "ReadOnlyRpcAttribute" 
                      || attr.GetType().Name == "ReadOnlyAttribute");

        var returnType = targetMethod.ReturnType;

        if (returnType == typeof(Task))
            return InvokeVoidAsync(request, isReadOnlyRpc);

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var invokeMethod = typeof(RpcProxy<TInterface>)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(m => m.Name == nameof(InvokeAsync) && m.IsGenericMethod);
            return invokeMethod
                .MakeGenericMethod(resultType)
                .Invoke(this, [request, isReadOnlyRpc]);
        }

        throw new NotSupportedException(
            $"Return type '{returnType.Name}' not supported. Use Task or Task<T>.");
    }

    private async Task InvokeVoidAsync(RpcRequest request, bool isReadOnlyRpc)
    {
        var requestBytes = request.ToByteArray();
        var responseBytes = isReadOnlyRpc 
            ? await _actor.InvokeReadOnlyRpcAsync(requestBytes)
            : await _actor.InvokeRpcAsync(requestBytes);
        var response = RpcResponse.Parser.ParseFrom(responseBytes);

        if (!response.Success)
            throw new InvalidOperationException(
                $"RPC call '{request.MethodName}' failed: {response.Error?.Message ?? "Unknown error"}");
    }

    private async Task<TResult> InvokeAsync<TResult>(RpcRequest request, bool isReadOnlyRpc)
    {
        var requestBytes = request.ToByteArray();
        var responseBytes = isReadOnlyRpc 
            ? await _actor.InvokeReadOnlyRpcAsync(requestBytes)
            : await _actor.InvokeRpcAsync(requestBytes);
        var response = RpcResponse.Parser.ParseFrom(responseBytes);

        if (!response.Success)
            throw new InvalidOperationException(
                $"RPC call '{request.MethodName}' failed: {response.Error?.Message ?? "Unknown error"}");

        // Handle null result (should be packed as Empty, but check for safety)
        if (response.Result == null)
            return default(TResult)!;

        return ProtobufPacker.Unpack<TResult>(response.Result);
    }
}

