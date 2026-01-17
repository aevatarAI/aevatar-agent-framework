using System.CommandLine;
using System.CommandLine.Invocation;

namespace Aevatar.Platform.Cli.Commands;

public static partial class RootCommands
{
    private static Command BuildMcpCommand()
    {
        var list = new Command("list", "List MCP servers");
        list.SetHandler(async (InvocationContext ctx) => await Handlers.ListMcpServersAsync(ctx));

        var addName = new Argument<string>("name", "MCP server name");
        var addUrl = new Option<string?>("--url", "Server URL (http/ws)");
        var addCommand = new Option<string?>("--command", "Server command (stdio)");
        var addTransport = new Option<string?>("--transport", "Transport: http | stdio");
        var add = new Command("add", "Add MCP server");
        add.AddArgument(addName);
        add.AddOption(addUrl);
        add.AddOption(addCommand);
        add.AddOption(addTransport);
        add.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(addName);
            var url = ctx.ParseResult.GetValueForOption(addUrl);
            var command = ctx.ParseResult.GetValueForOption(addCommand);
            var transport = ctx.ParseResult.GetValueForOption(addTransport);
            await Handlers.AddMcpServerAsync(ctx, name, url, command, transport);
        });

        var authName = new Argument<string?>("name", () => null, "MCP server name");
        var authToken = new Option<string?>("--token", "Credential/token");
        var authEnv = new Option<string?>("--from-env", "Read token from env var");
        var authStdin = new Option<bool>("--from-stdin", "Read token from STDIN");
        var auth = new Command("auth", "Authenticate MCP server");
        auth.AddArgument(authName);
        auth.AddOption(authToken);
        auth.AddOption(authEnv);
        auth.AddOption(authStdin);
        auth.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(authName);
            var token = ctx.ParseResult.GetValueForOption(authToken);
            var fromEnv = ctx.ParseResult.GetValueForOption(authEnv);
            var fromStdin = ctx.ParseResult.GetValueForOption(authStdin);
            await Handlers.SaveMcpCredentialAsync(ctx, name, token, fromEnv, fromStdin);
        });

        var authList = new Command("list", "List MCP auth status");
        authList.AddAlias("ls");
        authList.SetHandler(async (InvocationContext ctx) => await Handlers.ListMcpAuthAsync(ctx));
        auth.AddCommand(authList);

        var logoutName = new Argument<string?>("name", () => null, "MCP server name");
        var logout = new Command("logout", "Remove MCP credential");
        logout.AddArgument(logoutName);
        logout.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(logoutName);
            await Handlers.RemoveMcpCredentialAsync(ctx, name);
        });

        var debugName = new Argument<string?>("name", () => null, "MCP server name");
        var debug = new Command("debug", "Debug MCP server connection");
        debug.AddArgument(debugName);
        debug.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(debugName);
            await Handlers.DebugMcpServerAsync(ctx, name);
        });

        var mcp = new Command("mcp", "MCP servers");
        mcp.AddCommand(list);
        mcp.AddCommand(add);
        mcp.AddCommand(auth);
        mcp.AddCommand(logout);
        mcp.AddCommand(debug);
        return mcp;
    }

    private static Command BuildAuthCommand()
    {
        var provider = new Argument<string?>("provider", () => null, "Provider name");
        var apiKey = new Option<string?>("--api-key", "Provider API key");
        var fromEnv = new Option<string?>("--from-env", "Read API key from env var");
        var fromStdin = new Option<bool>("--from-stdin", "Read API key from STDIN");
        var login = new Command("login", "Authenticate provider");
        login.AddArgument(provider);
        login.AddOption(apiKey);
        login.AddOption(fromEnv);
        login.AddOption(fromStdin);
        login.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(provider);
            var key = ctx.ParseResult.GetValueForOption(apiKey);
            var env = ctx.ParseResult.GetValueForOption(fromEnv);
            var stdin = ctx.ParseResult.GetValueForOption(fromStdin);
            await Handlers.SaveProviderCredentialAsync(ctx, name, key, env, stdin);
        });

        var list = new Command("list", "List providers with credentials");
        list.AddAlias("ls");
        list.SetHandler(async (InvocationContext ctx) => await Handlers.ListProviderCredentialsAsync(ctx));

        var logoutProvider = new Argument<string?>("provider", () => null, "Provider name");
        var logout = new Command("logout", "Clear auth");
        logout.AddArgument(logoutProvider);
        logout.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(logoutProvider);
            await Handlers.RemoveProviderCredentialAsync(ctx, name);
        });

        var auth = new Command("auth", "Authentication");
        auth.AddCommand(login);
        auth.AddCommand(list);
        auth.AddCommand(logout);
        return auth;
    }

    private static Command BuildGithubCommand()
    {
        var installPath = new Option<string?>("--path", "Workflow path override");
        var force = new Option<bool>("--force", "Overwrite existing workflow file");
        var install = new Command("install", "Install GitHub workflow");
        install.AddOption(installPath);
        install.AddOption(force);
        install.SetHandler(async (InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForOption(installPath);
            var overwrite = ctx.ParseResult.GetValueForOption(force);
            await Handlers.InstallGithubWorkflowAsync(ctx, path, overwrite);
        });

        var eventPath = new Option<string?>("--event", "GitHub event payload path");
        var token = new Option<string?>("--token", "GitHub token (optional)");
        var run = new Command("run", "Run GitHub agent workflow");
        run.AddOption(eventPath);
        run.AddOption(token);
        run.SetHandler(async (InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForOption(eventPath);
            var auth = ctx.ParseResult.GetValueForOption(token);
            await Handlers.RunGithubAsync(ctx, path, auth);
        });

        var github = new Command("github", "GitHub automation");
        github.AddCommand(install);
        github.AddCommand(run);
        return github;
    }

    private static Command BuildAcpCommand()
    {
        var acp = new Command("acp", "Agent Control Plane (placeholder)");
        var cwd = new Option<string?>("--cwd", "Working directory");
        var host = new Option<string?>("--hostname", "Server hostname (reserved)");
        var port = new Option<int?>("--port", "Server port (reserved)");
        acp.AddOption(cwd);
        acp.AddOption(host);
        acp.AddOption(port);
        acp.SetHandler(async (InvocationContext ctx) =>
        {
            var dir = ctx.ParseResult.GetValueForOption(cwd);
            await Handlers.RunAcpAsync(ctx, dir);
        });
        return acp;
    }

    private static Command BuildUpgradeCommand()
    {
        var upgrade = new Command("upgrade", "Upgrade Aevatar CLI");
        var method = new Option<string?>("--method", "Upgrade method (brew|dotnet|manual)");
        upgrade.AddOption(method);
        upgrade.SetHandler(async (InvocationContext ctx) =>
        {
            var selected = ctx.ParseResult.GetValueForOption(method);
            await Handlers.RunUpgradeAsync(ctx, selected);
        });
        return upgrade;
    }

    private static Command BuildUninstallCommand()
    {
        var uninstall = new Command("uninstall", "Uninstall Aevatar CLI");
        var keepConfig = new Option<bool>("--keep-config", "Keep configuration files");
        keepConfig.AddAlias("-c");
        var keepData = new Option<bool>("--keep-data", "Keep session data");
        keepData.AddAlias("-d");
        var dryRun = new Option<bool>("--dry-run", "Show what would be removed");
        var force = new Option<bool>("--force", "Skip confirmation");
        force.AddAlias("-f");
        uninstall.AddOption(keepConfig);
        uninstall.AddOption(keepData);
        uninstall.AddOption(dryRun);
        uninstall.AddOption(force);
        uninstall.SetHandler(async (InvocationContext ctx) =>
        {
            var keepConfigValue = ctx.ParseResult.GetValueForOption(keepConfig);
            var keepDataValue = ctx.ParseResult.GetValueForOption(keepData);
            var isDryRun = ctx.ParseResult.GetValueForOption(dryRun);
            var skipConfirm = ctx.ParseResult.GetValueForOption(force);
            await Handlers.RunUninstallAsync(ctx, keepConfigValue, keepDataValue, isDryRun, skipConfirm);
        });
        return uninstall;
    }
}
