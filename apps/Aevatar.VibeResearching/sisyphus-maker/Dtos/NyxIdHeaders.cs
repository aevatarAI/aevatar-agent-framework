namespace SisyphusMaker.Dtos;

/// <summary>
/// NyxID header name constants injected by the NyxID MCP proxy on Flow A requests.
/// </summary>
public static class NyxIdHeaders
{
    public const string DelegationToken = "X-NyxID-Delegation-Token";
    public const string UserId = "X-NyxID-User-Id";
    public const string UserEmail = "X-NyxID-User-Email";
    public const string UserName = "X-NyxID-User-Name";
}
