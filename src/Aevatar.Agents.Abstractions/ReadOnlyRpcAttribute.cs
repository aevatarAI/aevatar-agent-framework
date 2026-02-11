namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Marks a RPC method as read-only (does not modify Agent state).
/// When used on interface methods, RpcProxy will route calls through
/// InvokeReadOnlyRpcAsync which has [AlwaysInterleave] on Orleans Grain,
/// allowing concurrent execution without blocking on other operations.
/// 
/// Use this for query/get operations that only read Agent state.
/// 
/// Note: This is NOT Orleans' [ReadOnly] attribute. Orleans' [ReadOnly] only works
/// on Grain implementation methods. Since our RPC mechanism goes through a single
/// InvokeRpcAsync entry point, Orleans cannot see the business method's attributes.
/// This attribute enables our framework to make the routing decision at RPC level.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ReadOnlyRpcAttribute : Attribute
{
}
