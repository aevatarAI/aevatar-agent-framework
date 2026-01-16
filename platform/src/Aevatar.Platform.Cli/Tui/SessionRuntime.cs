namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  SessionRuntime
//
//  说明：
//  - TUI 运行期会话上下文（session id / workflow / profile）
// ============================================================
public sealed class SessionRuntime
{
    public SessionRuntime(string sessionId, ulong seq, string profile, string workflow)
    {
        SessionId = sessionId;
        Seq = seq;
        Profile = profile;
        Workflow = workflow;
    }

    public string SessionId { get; }

    public ulong Seq { get; set; }

    public string Profile { get; set; }

    public string Workflow { get; set; }

    public List<string> AttachedFiles { get; } = [];
}


