namespace Aevatar.Trade.Tools;

public static class TradeDotNetSkillPaths
{
    public static string WeexGetBalances => Resolve("weex_get_balances.cs");
    public static string WeexGetOrder => Resolve("weex_get_order.cs");
    public static string WeexGetOpenOrders => Resolve("weex_get_open_orders.cs");
    public static string WeexPlaceOrder => Resolve("weex_place_order.cs");
    public static string WeexCancelOrder => Resolve("weex_cancel_order.cs");

    // ============================================================
    //  AI Wars (WEEX Alpha Awakens) - DotNet File Skills
    //
    //  设计原则：
    //  - 每个 API endpoint 对应一个 *.cs（便于最小权限、最小语义、可审计）
    //  - 运行时扫描目录，而不是在 C# 里手写 30+ 个常量路径（减少僵化）
    // ============================================================

    public static IReadOnlyList<string> WeexAiWarsAll => EnumerateUnder("ai-wars");

    // AI Wars: Upload AI Log (POST /capi/v2/order/uploadAiLog)
    public static string WeexAiWarsUploadAiLog => Resolve("ai-wars/upload/weex_ai_order_upload_ai_log.cs");

    private static string Resolve(string relativePath)
    {
        // Primary: copied to output directory by csproj (Tools/DotNetSkills -> <bin>/DotNetSkills)
        var fromOutput = Path.Combine(AppContext.BaseDirectory, "Tools", "DotNetSkills", relativePath);
        if (File.Exists(fromOutput))
            return fromOutput;

        // Backward/alternative layout: <bin>/DotNetSkills/<file>
        var fromOutputFlat = Path.Combine(AppContext.BaseDirectory, "DotNetSkills", relativePath);
        if (File.Exists(fromOutputFlat))
            return fromOutputFlat;

        // Fallback: repo-relative (useful when running from repo root)
        var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), "trade", "Aevatar.Trade", "Tools", "DotNetSkills", relativePath);
        if (File.Exists(fromCwd))
            return fromCwd;

        // Last resort: relative to this source file location is not available at runtime.
        return fromOutput;
    }

    private static IReadOnlyList<string> EnumerateUnder(string relativeDir)
    {
        // Primary: copied to output directory by csproj (Tools/DotNetSkills -> <bin>/Tools/DotNetSkills)
        var fromOutput = Path.Combine(AppContext.BaseDirectory, "Tools", "DotNetSkills", relativeDir);
        if (Directory.Exists(fromOutput))
            return Directory.GetFiles(fromOutput, "*.cs", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

        // Backward/alternative layout: <bin>/DotNetSkills
        var fromOutputFlat = Path.Combine(AppContext.BaseDirectory, "DotNetSkills", relativeDir);
        if (Directory.Exists(fromOutputFlat))
            return Directory.GetFiles(fromOutputFlat, "*.cs", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

        // Repo-relative fallback
        var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), "trade", "Aevatar.Trade", "Tools", "DotNetSkills", relativeDir);
        if (Directory.Exists(fromCwd))
            return Directory.GetFiles(fromCwd, "*.cs", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

        return Array.Empty<string>();
    }
}


