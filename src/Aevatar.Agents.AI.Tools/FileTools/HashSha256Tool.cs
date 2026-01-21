using System.Security.Cryptography;
using System.Text;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class HashSha256Tool : AevatarToolBase
{
    private readonly FileToolOptions _options;

    public HashSha256Tool(FileToolOptions options)
    {
        _options = options ?? FileToolOptions.Empty;
    }

    public override string Name => "hash_sha256";
    public override string Description => "Compute SHA-256 hash of text or file.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "hash", "crypto", "filesystem" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["text"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Text to hash."
                },
                ["path"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "File path to hash."
                },
                ["max_bytes"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Max bytes to read from file.",
                    DefaultValue = 5_000_000,
                    Minimum = 1024,
                    Maximum = 50_000_000
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (FileToolHelpers.TryGetString(parameters, "text", out var text))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = SHA256.HashData(bytes);
            return FileToolHelpers.ToStruct(new
            {
                ok = true,
                algorithm = "sha256",
                hash = Convert.ToHexString(hash).ToLowerInvariant()
            });
        }

        if (!FileToolHelpers.TryGetString(parameters, "path", out var rawPath))
        {
            return FileToolHelpers.ToStruct(new { ok = false, error = "text_or_path_required" });
        }

        if (!FileToolHelpers.TryResolvePath(_options, rawPath, _options.ReadRoots, out var full, out var reason))
        {
            return FileToolHelpers.ToStruct(new { ok = false, error = reason });
        }

        if (!File.Exists(full))
        {
            return FileToolHelpers.ToStruct(new { ok = false, error = "file_not_found", path = full });
        }

        var maxBytes = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_bytes"), 5_000_000, 1024, 50_000_000);
        var info = new FileInfo(full);
        if (info.Length > maxBytes)
        {
            return FileToolHelpers.ToStruct(new { ok = false, error = "file_too_large", path = full, size_bytes = info.Length });
        }

        await using var stream = File.OpenRead(full);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return FileToolHelpers.ToStruct(new
        {
            ok = true,
            algorithm = "sha256",
            hash = Convert.ToHexString(hashBytes).ToLowerInvariant(),
            path = full,
            size_bytes = info.Length
        });
    }

    protected override bool RequiresInternalAccess() => true;
}
