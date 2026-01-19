using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Platform.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Platform.Cli.Commands;

public static partial class RootCommands
{
    private static Command BuildConfigCommand()
    {
        var show = new Command("show", "Show config (secrets redacted)");
        show.SetHandler((InvocationContext ctx) =>
        {
            var effective = Handlers.LoadEffectiveConfig(ctx);
            Console.WriteLine(JsonSerializer.Serialize(effective.Config, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        });

        var edit = new Command("edit", "Edit config file");
        edit.SetHandler(async (InvocationContext ctx) => await Handlers.EditConfigAsync(ctx));

        var force = new Option<bool>("--force", "Overwrite existing config files");
        var init = new Command("init", "Initialize config in ~/.aevatar");
        init.AddOption(force);
        init.SetHandler(async (InvocationContext ctx) =>
        {
            var overwrite = ctx.ParseResult.GetValueForOption(force);
            await Handlers.InitConfigAsync(ctx, overwrite);
        });

        var dryRun = new Option<bool>("--dry-run", "Preview migration without writing");
        var migrate = new Command("migrate", "Migrate legacy config fields");
        migrate.AddOption(dryRun);
        migrate.SetHandler(async (InvocationContext ctx) =>
        {
            var preview = ctx.ParseResult.GetValueForOption(dryRun);
            await Handlers.MigrateConfigAsync(ctx, preview);
        });

        var validate = new Command("validate", "Validate config values");
        validate.SetHandler(async (InvocationContext ctx) => await Handlers.ValidateConfigAsync(ctx));

        var config = new Command("config", "Config management");
        config.AddCommand(show);
        config.AddCommand(edit);
        config.AddCommand(init);
        config.AddCommand(migrate);
        config.AddCommand(validate);
        return config;
    }

    private static Command BuildAgentsCommand()
    {
        var list = new Command("list", "List agent roles");
        list.SetHandler((InvocationContext ctx) =>
        {
            var dir = Handlers.GetAgentsDirectory(ctx);
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("(no agents)");
                return;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.yaml"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                Console.WriteLine(name);
            }
        });

        var showId = new Argument<string>("role", "Agent role name");
        var show = new Command("show", "Show agent role YAML");
        show.AddArgument(showId);
        show.SetHandler((InvocationContext ctx) =>
        {
            var role = ctx.ParseResult.GetValueForArgument(showId);
            var path = Path.Combine(Handlers.GetAgentsDirectory(ctx), $"{role}.yaml");
            if (!File.Exists(path))
            {
                Console.WriteLine($"Agent role '{role}' not found.");
                return;
            }

            Console.WriteLine(File.ReadAllText(path));
        });

        var createId = new Argument<string>("role", "Agent role name");
        var provider = new Option<string?>("--provider", "Provider override (optional)");
        var model = new Option<string?>("--model", "Model override (optional)");
        var force = new Option<bool>("--force", "Overwrite existing file");
        var edit = new Option<bool>("--edit", "Open editor after create");
        var create = new Command("create", "Create agent role YAML");
        create.AddArgument(createId);
        create.AddOption(provider);
        create.AddOption(model);
        create.AddOption(force);
        create.AddOption(edit);
        create.SetHandler(async (InvocationContext ctx) =>
        {
            var role = ctx.ParseResult.GetValueForArgument(createId);
            var providerValue = ctx.ParseResult.GetValueForOption(provider);
            var modelValue = ctx.ParseResult.GetValueForOption(model);
            var overwrite = ctx.ParseResult.GetValueForOption(force);
            var openEditor = ctx.ParseResult.GetValueForOption(edit);
            await Handlers.CreateAgentRoleAsync(ctx, role, providerValue, modelValue, overwrite, openEditor);
        });

        var agents = new Command("agents", "Agent role management");
        agents.AddAlias("agent");
        agents.AddCommand(list);
        agents.AddCommand(show);
        agents.AddCommand(create);
        return agents;
    }

    private static Command BuildWorkflowsCommand()
    {
        var list = new Command("list", "List workflow files");
        list.SetHandler((InvocationContext ctx) =>
        {
            var dir = Handlers.GetWorkflowsDirectory(ctx);
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("(no workflows)");
                return;
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                Console.WriteLine(Path.GetFileName(file));
            }
        });

        var fileArg = new Argument<string>("file", "Workflow file path (JSON/YAML)");
        var validate = new Command("validate", "Validate workflow DSL");
        validate.AddArgument(fileArg);
        validate.SetHandler((InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(fileArg);
            if (!File.Exists(path))
            {
                Console.WriteLine($"File not found: {path}");
                return;
            }

            var raw = File.ReadAllText(path);
            var roles = new GlobalAgentYamlRegistry(NullLogger<GlobalAgentYamlRegistry>.Instance);
            var compiler = new PlatformMeshCompiler(roles);
            var result = compiler.Compile(raw);

            if (result.Ok)
            {
                Console.WriteLine("OK");
                return;
            }

            foreach (var err in result.Errors)
            {
                var pathInfo = string.IsNullOrWhiteSpace(err.Path) ? "" : $" ({err.Path})";
                Console.WriteLine($"{err.Code}: {err.Message}{pathInfo}");
            }
        });

        var source = new Option<string?>("--source", "Source workflows directory (defaults to repo workflows)");
        var sync = new Command("sync", "Sync built-in workflows to ~/.aevatar/workflows");
        sync.AddOption(source);
        sync.SetHandler(async (InvocationContext ctx) =>
        {
            var sourceDir = ctx.ParseResult.GetValueForOption(source);
            await Handlers.SyncWorkflowsAsync(ctx, sourceDir);
        });

        var workflows = new Command("workflows", "Workflow management");
        workflows.AddAlias("workflow");
        workflows.AddCommand(list);
        workflows.AddCommand(validate);
        workflows.AddCommand(sync);
        return workflows;
    }

    private static Command BuildModelsCommand()
    {
        var providerArg = new Argument<string?>("provider", () => null, "Filter by provider");
        var providerOpt = new Option<string?>("--provider", "Filter by provider");
        var verbose = new Option<bool>("--verbose", "Show endpoint + default model");
        var refresh = new Option<bool>("--refresh", "Refresh model list (config-backed)");

        var list = new Command("list", "List model providers");
        list.AddArgument(providerArg);
        list.AddOption(providerOpt);
        list.AddOption(verbose);
        list.AddOption(refresh);
        list.SetHandler(async (InvocationContext ctx) =>
        {
            var provider = ctx.ParseResult.GetValueForOption(providerOpt)
                           ?? ctx.ParseResult.GetValueForArgument(providerArg);
            var isVerbose = ctx.ParseResult.GetValueForOption(verbose);
            var doRefresh = ctx.ParseResult.GetValueForOption(refresh);
            await Handlers.ListModelsAsync(ctx, provider, isVerbose, doRefresh);
        });

        var models = new Command("models", "Model providers");
        models.AddArgument(providerArg);
        models.AddOption(providerOpt);
        models.AddOption(verbose);
        models.AddOption(refresh);
        models.SetHandler(async (InvocationContext ctx) =>
        {
            var provider = ctx.ParseResult.GetValueForOption(providerOpt)
                           ?? ctx.ParseResult.GetValueForArgument(providerArg);
            var isVerbose = ctx.ParseResult.GetValueForOption(verbose);
            var doRefresh = ctx.ParseResult.GetValueForOption(refresh);
            await Handlers.ListModelsAsync(ctx, provider, isVerbose, doRefresh);
        });
        models.AddCommand(list);
        return models;
    }
}
