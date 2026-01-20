using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;

namespace Aevatar.Platform.Cli.Commands;

public static partial class RootCommands
{
    private static Command BuildSessionsCommand()
    {
        var maxCount = new Option<int?>("--max-count", "Limit to N most recent sessions");
        maxCount.AddAlias("-n");
        var format = new Option<string>("--format", () => "table", "Output format: table | json");
        format.FromAmong("table", "json");
        var list = new Command("list", "List sessions");
        list.AddOption(maxCount);
        list.AddOption(format);
        list.SetHandler(async (InvocationContext ctx) =>
        {
            var service = Handlers.CreateSessionService(ctx);
            var sessions = await service.ListSessionsAsync(ctx.GetCancellationToken());
            var limit = ctx.ParseResult.GetValueForOption(maxCount);
            if (limit.HasValue && limit.Value > 0)
                sessions = sessions.Take(limit.Value).ToList();

            if (sessions.Count == 0)
            {
                Console.WriteLine("(no sessions)");
                return;
            }

            var fmt = ctx.ParseResult.GetValueForOption(format);
            if (string.Equals(fmt, "json", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(JsonSerializer.Serialize(sessions, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                return;
            }

            foreach (var s in sessions)
            {
                var ts = s.LastActivityUtc?.ToString("O") ?? "-";
                Console.WriteLine($"{s.SessionId}\t{s.Profile}\t{s.ActiveWorkflow}\t{ts}");
            }
        });

        var showId = new Argument<string>("session-id", "Session ID");
        var show = new Command("show", "Show session state");
        show.AddArgument(showId);
        show.SetHandler(async (InvocationContext ctx) =>
        {
            var sessionId = ctx.ParseResult.GetValueForArgument(showId);
            var service = Handlers.CreateSessionService(ctx);
            var state = await service.GetSessionStateAsync(sessionId, ctx.GetCancellationToken());
            if (state == null)
            {
                Console.WriteLine($"Session '{sessionId}' not found.");
                return;
            }

            Console.WriteLine(JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        });

        var exportId = new Argument<string>("session-id", "Session ID");
        var exportPath = new Argument<string>("output", "Output file path");
        var export = new Command("export", "Export session events");
        export.AddArgument(exportId);
        export.AddArgument(exportPath);
        export.SetHandler(async (InvocationContext ctx) =>
        {
            var sessionId = ctx.ParseResult.GetValueForArgument(exportId);
            var path = ctx.ParseResult.GetValueForArgument(exportPath);
            await Handlers.ExportSessionAsync(ctx, sessionId, path);
        });

        var importPath = new Argument<string>("input", "Export file path");
        var overwrite = new Option<bool>("--overwrite", "Overwrite existing session if id collides");
        var import = new Command("import", "Import session events");
        import.AddArgument(importPath);
        import.AddOption(overwrite);
        import.SetHandler(async (InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(importPath);
            var overwriteExisting = ctx.ParseResult.GetValueForOption(overwrite);
            await Handlers.ImportSessionAsync(ctx, path, overwriteExisting);
        });

        var sessions = new Command("sessions", "Session management");
        sessions.AddAlias("session");
        sessions.AddCommand(list);
        sessions.AddCommand(show);
        sessions.AddCommand(export);
        sessions.AddCommand(import);

        return sessions;
    }

    private static Command BuildStatsCommand()
    {
        var stats = new Command("stats", "Show session stats");
        var days = new Option<int?>("--days", "Show stats for last N days");
        var tools = new Option<int>("--tools", () => 0, "Show top N tools (0 = all)");
        tools.Arity = ArgumentArity.ZeroOrOne;
        var models = new Option<int>("--models", () => 0, "Show top N models (0 = all)");
        models.Arity = ArgumentArity.ZeroOrOne;
        var project = new Option<string?>("--project", "Filter by working directory");
        stats.AddOption(days);
        stats.AddOption(tools);
        stats.AddOption(models);
        stats.AddOption(project);
        stats.SetHandler(async (InvocationContext ctx) =>
        {
            var dayCount = ctx.ParseResult.GetValueForOption(days);
            var toolCount = ctx.ParseResult.GetValueForOption(tools);
            var modelCount = ctx.ParseResult.GetValueForOption(models);
            var projectDir = ctx.ParseResult.GetValueForOption(project);
            await Handlers.ShowStatsAsync(ctx, dayCount, toolCount, modelCount, projectDir);
        });
        return stats;
    }

    private static Command BuildExportCommand()
    {
        var id = new Argument<string>("session-id", "Session ID");
        var path = new Argument<string>("output", "Output file path");
        var export = new Command("export", "Export session events");
        export.AddArgument(id);
        export.AddArgument(path);

        export.SetHandler(async (InvocationContext ctx) =>
        {
            var sessionId = ctx.ParseResult.GetValueForArgument(id);
            var output = ctx.ParseResult.GetValueForArgument(path);
            await Handlers.ExportSessionAsync(ctx, sessionId, output);
        });

        return export;
    }

    private static Command BuildImportCommand()
    {
        var path = new Argument<string>("input", "Export file path");
        var overwrite = new Option<bool>("--overwrite", "Overwrite existing session if id collides");
        var import = new Command("import", "Import session events");
        import.AddArgument(path);
        import.AddOption(overwrite);

        import.SetHandler(async (InvocationContext ctx) =>
        {
            var input = ctx.ParseResult.GetValueForArgument(path);
            var overwriteExisting = ctx.ParseResult.GetValueForOption(overwrite);
            await Handlers.ImportSessionAsync(ctx, input, overwriteExisting);
        });

        return import;
    }
}
