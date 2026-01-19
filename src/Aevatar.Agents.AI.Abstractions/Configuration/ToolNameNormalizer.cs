namespace Aevatar.Agents.AI.Abstractions.Configuration;

// ============================================================
//  ToolNameNormalizer
//
//  说明：
//  - YAML 工具名归一化（别名兼容）
//  - 放在 Abstractions，避免 AI.Core 绑定具体工具名
// ============================================================
public static class ToolNameNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Cursor compatibility + common aliasing
            ["read_file"] = "file_read",
            ["write_file"] = "file_write",
            ["delete_file"] = "file_delete",
            ["list_dir"] = "dir_list",
            ["glob_file_search"] = "glob",
            ["search_files"] = "glob",
            ["ast-grep"] = "ast_grep"
        };

    public static IReadOnlyList<string> NormalizeTools(IReadOnlyList<string> tools)
    {
        if (tools is { Count: > 0 })
        {
            var normalized = new List<string>(tools.Count);
            foreach (var tool in tools)
            {
                var name = NormalizeToolName(tool);
                if (name.Length > 0)
                    normalized.Add(name);
            }
            return normalized;
        }

        return tools ?? Array.Empty<string>();
    }

    public static string NormalizeToolName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;

        if (Aliases.TryGetValue(name, out var mapped))
            return mapped;

        if (name.Contains('-', StringComparison.Ordinal))
        {
            var dashed = name.Replace('-', '_');
            if (Aliases.TryGetValue(dashed, out var dashedMapped))
                return dashedMapped;
            return dashed;
        }

        return name;
    }
}
