using Aevatar.Agents.Core.Secrets;

namespace Aevatar.Agents.SecretsCli;

internal static class Program
{
    // ============================================================
    //  Aevatar Secrets CLI
    //
    //  Purpose:
    //  - Provide a simple, non-interactive way to manage encrypted user secrets.
    //  - Avoid copying appsettings.secrets.json across demos/apps.
    //
    //  Examples:
    //    # Set from env (recommended: avoids shell history)
    //    export DEEPSEEK_API_KEY="..."
    //    dotnet run --project src/Aevatar.Agents.SecretsCli -- set LLMProviders:Providers:deepseek:ApiKey --from-env DEEPSEEK_API_KEY
    //
    //    # Get
    //    dotnet run --project src/Aevatar.Agents.SecretsCli -- get LLMProviders:Providers:deepseek:ApiKey
    //
    //    # List keys
    //    dotnet run --project src/Aevatar.Agents.SecretsCli -- list
    //
    //  Path overrides:
    //    AEVATAR_SECRETS_PATH=/tmp/aevatar.secrets.json dotnet run --project ... -- list
    //    dotnet run --project ... -- --path /tmp/aevatar.secrets.json list
    // ============================================================

    private const string UsageText = """
Aevatar Secrets CLI

Usage:
  dotnet run --project src/Aevatar.Agents.SecretsCli -- [options] <command> [args]

Commands:
  set <key> <value>                Set a secret
  set <key> --from-env <ENV>       Set from environment variable (recommended)
  set <key> --from-stdin           Set from STDIN
  get <key>                        Get a secret value
  remove <key>                     Remove a secret
  list                             List all secret keys (values are not printed)

Options:
  --path <file>                    Override secrets file path
  --dir <dir>                      Override secrets directory (when --path not set)
  --no-keychain                    Disable macOS Keychain usage (fallback to file key)
  --help                           Show help

Notes:
  - Default secrets path: ~/.aevatar/secrets.json (encrypted)
  - Environment overrides: AEVATAR_SECRETS_PATH / AEVATAR_SECRETS_DIR
""";

    public static int Main(string[] args)
    {
        var parsed = CliArgs.Parse(args);
        if (parsed.ShowHelp || string.IsNullOrWhiteSpace(parsed.Command))
        {
            Console.WriteLine(UsageText);
            return 0;
        }

        var options = new AevatarUserSecretsOptions
        {
            SecretsPath = parsed.SecretsPath,
            SecretsDirectory = parsed.SecretsDir,
            PreferOsKeyStore = !parsed.NoKeychain
        };

        var store = new FileAevatarUserSecretsStore(options);
        var cmd = parsed.Command!.ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "set":
                {
                    if (parsed.Positionals.Count < 2)
                    {
                        // Allow: set <key> with value from env/stdin.
                        if (parsed.Positionals.Count < 1)
                        {
                            Console.Error.WriteLine("ERROR: missing <key>.");
                            return 2;
                        }
                    }

                    var key = parsed.Positionals[0].Trim();
                    var value = ResolveValueForSet(parsed, fallbackPositionalIndex: 1);

                    store.Set(key, value);
                    Console.WriteLine("OK");
                    return 0;
                }
                case "get":
                {
                    if (parsed.Positionals.Count < 1)
                    {
                        Console.Error.WriteLine("ERROR: missing <key>.");
                        return 2;
                    }

                    var key = parsed.Positionals[0].Trim();
                    if (!store.TryGet(key, out var value))
                    {
                        Console.Error.WriteLine("NOT_FOUND");
                        return 3;
                    }

                    Console.WriteLine(value);
                    return 0;
                }
                case "remove":
                case "rm":
                case "delete":
                case "del":
                {
                    if (parsed.Positionals.Count < 1)
                    {
                        Console.Error.WriteLine("ERROR: missing <key>.");
                        return 2;
                    }

                    var key = parsed.Positionals[0].Trim();
                    var removed = store.Remove(key);
                    Console.WriteLine(removed ? "OK" : "NOT_FOUND");
                    return removed ? 0 : 3;
                }
                case "list":
                case "ls":
                {
                    var all = store.GetAll();
                    foreach (var k in all.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine(k);
                    }
                    return 0;
                }
                default:
                    Console.Error.WriteLine($"ERROR: unknown command '{parsed.Command}'.");
                    Console.WriteLine(UsageText);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            // Never echo secret values; only show message.
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveValueForSet(CliArgs parsed, int fallbackPositionalIndex)
    {
        if (!string.IsNullOrWhiteSpace(parsed.FromEnv))
        {
            var envName = parsed.FromEnv.Trim();
            var v = Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrEmpty(v))
                throw new InvalidOperationException($"Environment variable '{envName}' is empty or not set.");
            return v;
        }

        if (parsed.FromStdin)
        {
            var v = Console.In.ReadToEnd();
            if (string.IsNullOrEmpty(v))
                throw new InvalidOperationException("STDIN is empty.");
            return v.TrimEnd('\r', '\n');
        }

        if (parsed.Positionals.Count <= fallbackPositionalIndex)
            throw new InvalidOperationException("Missing <value>. Provide it as an argument, or use --from-env/--from-stdin.");

        return parsed.Positionals[fallbackPositionalIndex];
    }

    private static string? TakeOptionValue(Dictionary<string, string?> options, params string[] names)
    {
        foreach (var name in names)
        {
            if (options.TryGetValue(name, out var v))
                return v;
        }

        return null;
    }

    private static bool HasFlag(HashSet<string> flags, params string[] names)
    {
        foreach (var name in names)
        {
            if (flags.Contains(name))
                return true;
        }

        return false;
    }

    private sealed class CliArgs
    {
        public static CliArgs Parse(string[] args)
        {
            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var positionals = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i] ?? string.Empty;
                if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    positionals.Add(a);
                    continue;
                }

                var key = a.Trim();
                // Flags
                if (string.Equals(key, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "--h", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "--from-stdin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "--no-keychain", StringComparison.OrdinalIgnoreCase))
                {
                    flags.Add(key);
                    continue;
                }

                // Options expecting a value
                if (i + 1 >= args.Length)
                {
                    flags.Add("--help");
                    break;
                }

                var value = args[++i];
                options[key] = value;
            }

            var cmd = positionals.Count > 0 ? positionals[0] : null;
            var rest = positionals.Count > 1 ? positionals.Skip(1).ToList() : new List<string>();

            return new CliArgs
            {
                Command = cmd,
                Positionals = rest,
                ShowHelp = HasFlag(flags, "--help", "--h") || string.IsNullOrWhiteSpace(cmd),
                SecretsPath = TakeOptionValue(options, "--path"),
                SecretsDir = TakeOptionValue(options, "--dir"),
                FromEnv = TakeOptionValue(options, "--from-env"),
                FromStdin = HasFlag(flags, "--from-stdin"),
                NoKeychain = HasFlag(flags, "--no-keychain")
            };
        }

        public required string? Command { get; init; }
        public required List<string> Positionals { get; init; }
        public required bool ShowHelp { get; init; }

        public required string? SecretsPath { get; init; }
        public required string? SecretsDir { get; init; }

        public required string? FromEnv { get; init; }
        public required bool FromStdin { get; init; }
        public required bool NoKeychain { get; init; }
    }
}

