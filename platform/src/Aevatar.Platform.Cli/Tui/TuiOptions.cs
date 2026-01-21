namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  TuiOptions
//
//  说明：
//  - CLI 传入的运行参数（profile/workflow/model/paths）
// ============================================================
public sealed record TuiOptions(
    string? Profile,
    string? Workflow,
    string? Model,
    string? Provider,
    bool Resume,
    string? SessionId = null,
    string? WorkingDirectory = null,
    string? ConfigDir = null,
    string? ConfigPath = null,
    string? SecretsPath = null);


