using Aevatar.Agents.AI.Core;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Workflow;

namespace Aevatar.Platform.Core.Tools.Plugins;

// ============================================================
//  PlatformToolPluginLoader
//
//  说明：
//  - 将 ~/.aevatar/tools 与 ./aevatar/tools 中的 dotnet-file 工具注册到 Agent
//  - 通过 Tools.Plugins 控制开关与参数
// ============================================================
public sealed class PlatformToolPluginLoader
{
    private readonly PlatformToolPluginCatalog _catalog = new();

    public async Task RegisterAsync(
        AIGAgentBase agent,
        WorkflowRunInput input,
        ToolsConfig tools,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tools);

        if (!tools.Plugins.Enabled)
            return;

        var includeDotNet = tools.Plugins.IncludeDotNetFileTools;
        var includePython = tools.Plugins.IncludePythonFileTools;
        if (!includeDotNet && !includePython)
            return;

        var dirs = _catalog.ResolveToolDirectories(input, tools);
        if (dirs.Count == 0)
            return;

        var maxFiles = tools.Plugins.MaxFilesPerType;
        if (maxFiles <= 0)
            maxFiles = 64;

        foreach (var dir in dirs)
        {
            ct.ThrowIfCancellationRequested();
            await agent.RegisterFileSkillsFromDirectoryAsync(
                directoryPath: dir,
                includeDotNet: includeDotNet,
                includePython: includePython,
                searchOption: SearchOption.TopDirectoryOnly,
                maxFilesPerType: maxFiles,
                requireManifestMarker: tools.Plugins.RequireManifestMarker,
                cancellationToken: ct);
        }
    }
}
