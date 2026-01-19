using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class EnvGetTool : AevatarToolBase
{
    public override string Name => "env_get";
    public override string Description => "Read environment variables by name.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "env", "config", "utility" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["name"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Single environment variable name."
                },
                ["names"] = new ToolParameter
                {
                    Type = "array",
                    Required = false,
                    Description = "List of environment variable names.",
                    Items = new ToolParameter { Type = "string" }
                }
            }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var names = new List<string>();

        if (parameters.TryGetValue("names", out var rawList) && rawList is IEnumerable<object> list)
        {
            foreach (var item in list)
            {
                var name = item?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }
        }

        if (names.Count == 0 && parameters.TryGetValue("name", out var raw))
        {
            var name = raw?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name.Trim());
        }

        if (names.Count == 0)
        {
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
            {
                ok = false,
                error = "name_required"
            }));
        }

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            result[name] = Environment.GetEnvironmentVariable(name);
        }

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            values = result
        }));
    }

    protected override bool RequiresInternalAccess() => true;
}
