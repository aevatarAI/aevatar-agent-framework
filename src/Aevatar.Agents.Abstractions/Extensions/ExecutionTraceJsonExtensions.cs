using Google.Protobuf;

namespace Aevatar.Agents.Abstractions.Tracing;

// ============================================================
//  ExecutionTrace JSON Helpers
//
//  WHY:
//  - Trace is a Protobuf contract; JSON is for exporting/humans/UI.
//  - Keep formatting/parsing in one place to avoid scattered JsonFormatter usage.
// ============================================================
public static class ExecutionTraceJsonExtensions
{
    public static string ToJsonString(this ExecutionTrace trace)
    {
        return JsonFormatter.Default.Format(trace);
    }

    public static ExecutionTrace ParseExecutionTraceJson(string json)
    {
        return JsonParser.Default.Parse<ExecutionTrace>(json);
    }
}


