using System.CommandLine;
using System.CommandLine.Invocation;

namespace Aevatar.Platform.Cli.Commands;

public static partial class RootCommands
{
    private static Command BuildTuiCommand()
    {
        var projectArg = new Argument<string?>("project", () => null, "Project directory (default: current)");
        var cmd = new Command("tui", "Start terminal UI");
        cmd.AddArgument(projectArg);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var project = ctx.ParseResult.GetValueForArgument(projectArg);
            await Handlers.StartTuiAsync(ctx, project);
        });

        return cmd;
    }

    private static Command BuildRunCommand()
    {
        var promptArg = new Argument<string?>("prompt", () => null, "Prompt to run once");
        var session = new Option<string?>("--session", "Session ID to continue");
        session.AddAlias("-s");
        var title = new Option<string?>("--title", "Session title (stored in tags.title)");
        var share = new Option<bool>("--share", "Mark session as shared (tag only)");
        var format = new Option<string>("--format", () => "default", "Output format: default | json");
        format.FromAmong("default", "json");
        var attach = new Option<string?>("--attach", "Attach to a running server (base URL)");
        var hostname = new Option<string?>("--hostname", "Server hostname override (attach)");
        var port = new Option<int?>("--port", "Server port override (attach)");
        var cwd = new Option<string?>("--dir", "Working directory");
        var files = new Option<string[]>("--file", "Attach file(s) to the prompt")
        {
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };
        files.AddAlias("-f");
        format.AddAlias("--output");
        var cmd = new Command("run", "Run a single task and exit");
        cmd.AddArgument(promptArg);
        cmd.AddOption(session);
        cmd.AddOption(title);
        cmd.AddOption(share);
        cmd.AddOption(format);
        cmd.AddOption(attach);
        cmd.AddOption(hostname);
        cmd.AddOption(port);
        cmd.AddOption(cwd);
        cmd.AddOption(files);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var prompt = ctx.ParseResult.GetValueForArgument(promptArg);
            var options = Handlers.BuildRunOptions(
                ctx.ParseResult,
                prompt,
                session,
                title,
                share,
                format,
                attach,
                hostname,
                port,
                cwd,
                files);
            await Handlers.RunOnceAsync(ctx, options);
        });

        return cmd;
    }

    private static Command BuildServeCommand()
    {
        var port = new Option<int>("--port", () => 5678, "Server port (must not be 5000)");
        var host = new Option<string>("--hostname", () => "127.0.0.1", "Server hostname");
        host.AddAlias("--host");
        var cors = new Option<string[]>("--cors", "Additional CORS origins")
        {
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };
        var mdns = new Option<bool>("--mdns", "Enable mDNS (no-op placeholder)");
        var token = new Option<string?>("--token", "Server auth token");
        var cmd = new Command("serve", "Start server for attach/web");
        cmd.AddOption(host);
        cmd.AddOption(port);
        cmd.AddOption(cors);
        cmd.AddOption(mdns);
        cmd.AddOption(token);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var options = Handlers.BuildServerOptions(ctx.ParseResult, host, port, cors, mdns, token);
            await Handlers.RunServerAsync(options, openBrowser: false, ctx.GetCancellationToken());
        });

        return cmd;
    }

    private static Command BuildWebCommand()
    {
        var port = new Option<int>("--port", () => 5678, "Server port (must not be 5000)");
        var host = new Option<string>("--hostname", () => "127.0.0.1", "Server hostname");
        host.AddAlias("--host");
        var cors = new Option<string[]>("--cors", "Additional CORS origins")
        {
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };
        var mdns = new Option<bool>("--mdns", "Enable mDNS (no-op placeholder)");
        var token = new Option<string?>("--token", "Server auth token");
        var cmd = new Command("web", "Start server and open web endpoint");
        cmd.AddOption(host);
        cmd.AddOption(port);
        cmd.AddOption(cors);
        cmd.AddOption(mdns);
        cmd.AddOption(token);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var options = Handlers.BuildServerOptions(ctx.ParseResult, host, port, cors, mdns, token);
            await Handlers.RunServerAsync(options, openBrowser: true, ctx.GetCancellationToken());
        });
        return cmd;
    }

    private static Command BuildAttachCommand()
    {
        var urlArg = new Argument<string?>("url", () => null, "Server base URL");
        var session = new Option<string?>("--session", "Session ID");
        session.AddAlias("-s");
        var dir = new Option<string?>("--dir", "Working directory");
        var token = new Option<string?>("--token", "Server auth token");
        var cmd = new Command("attach", "Attach to a running server session");
        cmd.AddArgument(urlArg);
        cmd.AddOption(session);
        cmd.AddOption(dir);
        cmd.AddOption(token);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var target = ctx.ParseResult.GetValueForArgument(urlArg);
            var sid = ctx.ParseResult.GetValueForOption(session);
            var workingDir = ctx.ParseResult.GetValueForOption(dir);
            var authToken = ctx.ParseResult.GetValueForOption(token);
            await Handlers.AttachAsync(ctx, target, sid, workingDir, authToken);
        });

        return cmd;
    }
}
