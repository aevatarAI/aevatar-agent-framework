using System.CommandLine;
using Aevatar.Platform.Cli.Commands;
using Aevatar.Platform.Core;

// ============================================================
//  Aevatar.Platform CLI
//
//  说明：
//  - OpenCode parity 的命令面由 RootCommands 构建
//  - 默认进入 TUI；-c/--command 走单次运行
// ============================================================

var root = RootCommands.BuildRootCommand();
return await root.InvokeAsync(args);


