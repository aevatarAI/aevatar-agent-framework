using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Config.Mongo;

namespace Aevatar.Agents.Core.Secrets;

// ============================================================
//  Aevatar User Secrets (Encrypted, Per-User)
//
//  Design goals:
//  - Framework-level: demos/apps should NOT copy appsettings.secrets.json around.
//  - Merge-friendly: stored as IConfiguration-style key/value pairs.
//  - Safe defaults: do not log secrets; best-effort loading.
//  - Cross-platform: store encrypted payload on disk; master key is best-effort protected.
//
//  Default location:
//  - macOS/Linux: ~/.aevatar/secrets.json (configurable via env)
//  - Windows:     %USERPROFILE%\.aevatar\secrets.json (configurable via env)
//
//  Environment overrides:
//  - AEVATAR_SECRETS_PATH: full path to secrets file
//  - AEVATAR_SECRETS_DIR:  directory where secrets.json (and master key file) live
// ============================================================

public sealed class AevatarUserSecretsOptions
{
    public const string DefaultRootDirectoryName = ".aevatar";
    public const string DefaultSecretsFileName = "secrets.json";
    public const string DefaultConfigFileName = "config.json";
    public const string DefaultMasterKeyFileName = "masterkey.bin";

    /// <summary>
    /// Full path to secrets file. If set, overrides <see cref="SecretsDirectory"/>.
    /// </summary>
    public string? SecretsPath { get; set; }

    /// <summary>
    /// Full path to config file. If set, overrides <see cref="SecretsDirectory"/>.
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Directory where secrets file is placed. Defaults to "~/.aevatar".
    /// </summary>
    public string? SecretsDirectory { get; set; }

    /// <summary>
    /// Secrets file name under <see cref="SecretsDirectory"/>.
    /// </summary>
    public string SecretsFileName { get; set; } = DefaultSecretsFileName;

    /// <summary>
    /// Config file name under <see cref="SecretsDirectory"/>.
    /// </summary>
    public string ConfigFileName { get; set; } = DefaultConfigFileName;

    /// <summary>
    /// Master key file name under <see cref="SecretsDirectory"/> (fallback when OS key store is unavailable).
    /// </summary>
    public string MasterKeyFileName { get; set; } = DefaultMasterKeyFileName;

    /// <summary>
    /// Prefer OS key store for master key when available (best-effort).
    /// - macOS: Keychain via /usr/bin/security (with timeout)
    /// - Windows/Linux: fallback to file-based key by default
    /// </summary>
    public bool PreferOsKeyStore { get; set; } = true;

    /// <summary>
    /// Whether configuration providers should watch the secrets file and trigger reload.
    /// <para/>
    /// NOTE:
    /// - This only impacts IConfiguration reload.
    /// - Call sites still need to use IOptionsMonitor (or re-resolve providers) to observe updates.
    /// </summary>
    public bool ReloadOnChange { get; set; } = true;

    /// <summary>
    /// Max time to wait for OS key store commands (avoid hanging app startup).
    /// </summary>
    public TimeSpan OsKeyStoreTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// MongoDB connection string. If set, configuration will also be read/written to MongoDB.
    /// </summary>
    public string? MongoConnectionString { get; set; }

    /// <summary>
    /// MongoDB database name. Defaults to "aevatar_config".
    /// </summary>
    public string MongoDatabaseName { get; set; } = "aevatar_config";

    internal string ResolveSecretsPath()
    {
        if (!string.IsNullOrWhiteSpace(SecretsPath))
            return Path.GetFullPath(SecretsPath.Trim());

        var envPath = Environment.GetEnvironmentVariable(AevatarAgentsConstants.SecretsPathEnv);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            try
            {
                return Path.GetFullPath(envPath.Trim());
            }
            catch
            {
                // ignore invalid env value; fallthrough
            }
        }

        var dir = ResolveDirectory();
        return Path.Combine(dir, string.IsNullOrWhiteSpace(SecretsFileName) ? DefaultSecretsFileName : SecretsFileName.Trim());
    }

    internal string ResolveConfigPath()
    {
        if (!string.IsNullOrWhiteSpace(ConfigPath))
            return Path.GetFullPath(ConfigPath.Trim());

        var dir = ResolveDirectory();
        return Path.Combine(dir, string.IsNullOrWhiteSpace(ConfigFileName) ? DefaultConfigFileName : ConfigFileName.Trim());
    }

    internal string ResolveMasterKeyPath(string secretsPath)
    {
        var dir = Path.GetDirectoryName(secretsPath);
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.GetTempPath();

        var file = string.IsNullOrWhiteSpace(MasterKeyFileName) ? DefaultMasterKeyFileName : MasterKeyFileName.Trim();
        return Path.Combine(dir, file);
    }

    private string ResolveDirectory()
    {
        var dir = SecretsDirectory;
        if (string.IsNullOrWhiteSpace(dir))
        {
            var envDir = Environment.GetEnvironmentVariable(AevatarAgentsConstants.SecretsDirEnv);
            dir = string.IsNullOrWhiteSpace(envDir) ? null : envDir.Trim();
        }

        if (string.IsNullOrWhiteSpace(dir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                home = Path.GetTempPath();
            dir = Path.Combine(home, DefaultRootDirectoryName);
        }

        return Path.GetFullPath(dir.Trim());
    }

    /// <summary>
    /// Initialize the ~/.aevatar directory with default structure and CONFIG.md.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    public void EnsureDirectoryInitialized()
    {
        var dir = ResolveDirectory();
        
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            TrySetDirectoryPermissions(dir);
        }

        var subdirs = new[] { "agents", "skills", "workflows", "mcp", "logs" };
        foreach (var subdir in subdirs)
        {
            var path = Path.Combine(dir, subdir);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        var configMdPath = Path.Combine(dir, "CONFIG.md");
        if (!File.Exists(configMdPath))
        {
            TryWriteConfigMd(configMdPath);
        }
    }

    private static void TrySetDirectoryPermissions(string dir)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(dir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch
        {
            // best-effort only
        }
    }

    private static void TryWriteConfigMd(string path)
    {
        try
        {
            var content = GetEmbeddedConfigMd();
            if (!string.IsNullOrEmpty(content))
            {
                File.WriteAllText(path, content);
            }
        }
        catch
        {
            // best-effort only
        }
    }

    private static string? GetEmbeddedConfigMd()
    {
        var assembly = typeof(AevatarUserSecretsOptions).Assembly;
        var resourceName = "Aevatar.Agents.Core.Resources.CONFIG.md";
        
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}

public interface IAevatarUserSecretsStore
{
    IReadOnlyDictionary<string, string> GetAll();
    bool TryGet(string key, out string value);
    void Set(string key, string value);
    bool Remove(string key);
}

/// <summary>
/// File-backed user secrets store with AES-GCM encryption (payload at rest).
/// </summary>
public sealed class FileAevatarUserSecretsStore : IAevatarUserSecretsStore
{
    private const int KeyBytes = 32; // AES-256
    private const int NonceBytes = 12; // AesGcm recommended
    private const int TagBytes = 16; // default tag size
    private const string Aad = "aevatar-user-secrets-v1";
    private const string LlmDefaultProviderKey = "LLMProviders:Default";
    private const string LlmProviderPrefix = "LLMProviders:Providers:";
    private const string LlmProviderApiKeySuffix = ":ApiKey";

    private readonly AevatarUserSecretsOptions _options;
    private readonly MongoConfigStore _mongoStore;
    private readonly object _gate = new();

    public FileAevatarUserSecretsStore(AevatarUserSecretsOptions? options = null)
    {
        _options = options ?? new AevatarUserSecretsOptions();
        _mongoStore = new MongoConfigStore(_options);
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        lock (_gate)
        {
            var dict = TryLoadUnsafe() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Best-effort normalization:
            // - Avoid treating a literal "default" provider instance name as a magic fallback.
            // - If users have configured at least one provider API key but forgot to set LLMProviders:Default,
            //   we pick a stable default and persist it back into the encrypted secrets file.
            //
            // This makes downstream apps more predictable without requiring per-app appsettings.secrets.json.
            if (TryEnsureDefaultLlmProviderUnsafe(dict))
            {
                try
                {
                    SaveUnsafe(dict);
                }
                catch
                {
                    // best-effort only (never break reads)
                }
            }

            return dict;
        }
    }

    public bool TryGet(string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var all = GetAll();
        if (!all.TryGetValue(key.Trim(), out var v))
            return false;

        value = v;
        return true;
    }

    public void Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secret key cannot be empty.", nameof(key));

        lock (_gate)
        {
            var dict = TryLoadUnsafe() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            dict[key.Trim()] = value ?? string.Empty;
            SaveUnsafe(dict);
        }
    }

    public bool Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        lock (_gate)
        {
            var dict = TryLoadUnsafe();
            if (dict == null)
                return false;

            var removed = dict.Remove(key.Trim());
            if (removed)
            {
                SaveUnsafe(dict);
            }

            return removed;
        }
    }

    private static bool TryEnsureDefaultLlmProviderUnsafe(Dictionary<string, string> dict)
    {
        // We only set default when:
        // - LLMProviders:Default is missing/empty, OR
        // - it points to a provider that no longer has an ApiKey configured.
        //
        // Never overwrite an explicit, valid default.
        if (dict == null)
            return false;

        dict.TryGetValue(LlmDefaultProviderKey, out var rawDefault);
        var current = (rawDefault ?? string.Empty).Trim();

        // Treat "default" as a placeholder only when there is no real "default" instance configured.
        var currentLooksLikePlaceholder =
            string.IsNullOrWhiteSpace(current) ||
            (string.Equals(current, "default", StringComparison.OrdinalIgnoreCase) && !HasApiKey(dict, "default"));

        if (!currentLooksLikePlaceholder && HasApiKey(dict, current))
            return false;

        var configured = new List<string>(capacity: 8);
        foreach (var kv in dict)
        {
            var k = kv.Key ?? string.Empty;
            if (!k.StartsWith(LlmProviderPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!k.EndsWith(LlmProviderApiKeySuffix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;

            var mid = k.Substring(
                LlmProviderPrefix.Length,
                k.Length - LlmProviderPrefix.Length - LlmProviderApiKeySuffix.Length);
            mid = mid.Trim();
            if (mid.Length == 0)
                continue;

            configured.Add(mid);
        }

        if (configured.Count == 0)
        {
            // No providers configured: remove stale default if present.
            if (!string.IsNullOrWhiteSpace(current) && dict.Remove(LlmDefaultProviderKey))
                return true;
            return false;
        }

        configured.Sort(StringComparer.OrdinalIgnoreCase);
        var next = configured[0];
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
            return false;

        dict[LlmDefaultProviderKey] = next;
        return true;
    }

    private static bool HasApiKey(Dictionary<string, string> dict, string providerName)
    {
        providerName = (providerName ?? string.Empty).Trim();
        if (providerName.Length == 0)
            return false;

        var keyPath = $"{LlmProviderPrefix}{providerName}{LlmProviderApiKeySuffix}";
        return dict.TryGetValue(keyPath, out var v) && !string.IsNullOrWhiteSpace(v);
    }

    // ============================================================
    //  IO (best-effort load, strict save)
    // ============================================================

    private Dictionary<string, string>? TryLoadUnsafe()
    {
        var secretsPath = _options.ResolveSecretsPath();
        string? text = null;

        if (File.Exists(secretsPath))
        {
            try
            {
                text = File.ReadAllText(secretsPath, Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        // Fallback to Mongo if file is missing or empty
        if (string.IsNullOrWhiteSpace(text) && _mongoStore.IsAvailable)
        {
            text = _mongoStore.LoadSecrets();
        }

        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(text, JsonOptions);
            if (envelope == null || envelope.SchemaVersion != 1)
                return null;

            var plaintext = TryDecryptWithExistingKeys(envelope, secretsPath);
            if (plaintext == null || plaintext.Length == 0)
                return null;

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext, JsonOptions);
            return dict == null
                ? null
                : new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Best-effort: corrupted/unreadable secrets should not crash the app.
            return null;
        }
    }

    private void SaveUnsafe(Dictionary<string, string> dict)
    {
        var secretsPath = _options.ResolveSecretsPath();
        var dir = Path.GetDirectoryName(secretsPath);
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException($"Invalid secrets path: '{secretsPath}'");

        Directory.CreateDirectory(dir);
        TryHardenDirectory(dir);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(dict, JsonOptions);
        var key = GetOrCreateMasterKey(secretsPath);
        var envelope = Encrypt(plaintext, key);
        var json = JsonSerializer.Serialize(envelope, JsonOptions);

        // Atomic-ish write: temp then replace/move.
        var tmp = Path.Combine(dir, $"{Path.GetFileName(secretsPath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tmp, json, Encoding.UTF8);
        TryHardenFile(tmp);

        try
        {
            File.Move(tmp, secretsPath, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }

        TryHardenFile(secretsPath);

        // Also save to Mongo if available
        if (_mongoStore.IsAvailable)
        {
            _mongoStore.SaveSecrets(json);
        }
    }

    // ============================================================
    //  Crypto
    // ============================================================

    private static EncryptedEnvelope Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using var gcm = new AesGcm(key, TagBytes);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(Aad));

        return new EncryptedEnvelope
        {
            SchemaVersion = 1,
            Algorithm = "A256GCM",
            NonceB64 = Convert.ToBase64String(nonce),
            TagB64 = Convert.ToBase64String(tag),
            CiphertextB64 = Convert.ToBase64String(ciphertext)
        };
    }

    private static byte[]? TryDecrypt(EncryptedEnvelope env, byte[] key)
    {
        try
        {
            var nonce = Convert.FromBase64String(env.NonceB64 ?? "");
            var tag = Convert.FromBase64String(env.TagB64 ?? "");
            var ciphertext = Convert.FromBase64String(env.CiphertextB64 ?? "");

            if (nonce.Length != NonceBytes || tag.Length != TagBytes)
                return null;

            var plaintext = new byte[ciphertext.Length];
            using var gcm = new AesGcm(key, TagBytes);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(Aad));
            return plaintext;
        }
        catch
        {
            return null;
        }
    }

    private byte[]? TryDecryptWithExistingKeys(EncryptedEnvelope env, string secretsPath)
    {
        // Priority:
        // - macOS keychain (if it already has a key)
        // - file key (adjacent masterkey.bin)
        if (_options.PreferOsKeyStore && OperatingSystem.IsMacOS())
        {
            if (MacKeychain.TryGetExistingMasterKey(_options.OsKeyStoreTimeout, out var keychainKey))
            {
                var pt = TryDecrypt(env, keychainKey);
                if (pt != null)
                    return pt;
            }
        }

        if (TryLoadFileMasterKey(secretsPath, out var fileKey))
        {
            return TryDecrypt(env, fileKey);
        }

        return null;
    }

    private byte[] GetOrCreateMasterKey(string secretsPath)
    {
        if (_options.PreferOsKeyStore && OperatingSystem.IsMacOS())
        {
            if (MacKeychain.TryGetOrCreateMasterKey(_options.OsKeyStoreTimeout, out var key))
                return key;
        }

        // Fallback: file-based key under secrets directory.
        if (TryLoadFileMasterKey(secretsPath, out var existing))
            return existing;

        var created = RandomNumberGenerator.GetBytes(KeyBytes);
        var keyPath = _options.ResolveMasterKeyPath(secretsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        TryHardenDirectory(Path.GetDirectoryName(keyPath)!);

        File.WriteAllBytes(keyPath, created);
        TryHardenFile(keyPath);
        return created;
    }

    private bool TryLoadFileMasterKey(string secretsPath, out byte[] key)
    {
        key = Array.Empty<byte>();
        try
        {
            var keyPath = _options.ResolveMasterKeyPath(secretsPath);
            if (!File.Exists(keyPath))
                return false;

            var bytes = File.ReadAllBytes(keyPath);
            if (bytes.Length != KeyBytes)
                return false;

            key = bytes;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    //  Filesystem hardening (best-effort)
    // ============================================================

    private static void TryHardenDirectory(string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return;

            // 0700
            File.SetUnixFileMode(dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            // best-effort only
        }
    }

    private static void TryHardenFile(string file)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return;

            // 0600
            File.SetUnixFileMode(file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // best-effort only
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch
        {
            // ignore
        }
    }

    // ============================================================
    //  Keychain (macOS) - best-effort, time-bounded
    // ============================================================

    private static class MacKeychain
    {
        private const string SecurityBin = "/usr/bin/security";
        private const string Service = "aevatar-agent-framework";
        private const string Account = "aevatar-masterkey";

        public static bool TryGetExistingMasterKey(TimeSpan timeout, out byte[] key)
        {
            key = Array.Empty<byte>();
            if (!OperatingSystem.IsMacOS() || !File.Exists(SecurityBin))
                return false;

            var output = TryRunSecurity(new[]
            {
                "find-generic-password", "-a", Account, "-s", Service, "-w"
            }, timeout);

            if (string.IsNullOrWhiteSpace(output))
                return false;

            try
            {
                var raw = output.Trim();
                var bytes = Convert.FromBase64String(raw);
                if (bytes.Length != KeyBytes)
                    return false;

                key = bytes;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetOrCreateMasterKey(TimeSpan timeout, out byte[] key)
        {
            if (TryGetExistingMasterKey(timeout, out key))
                return true;

            if (!OperatingSystem.IsMacOS() || !File.Exists(SecurityBin))
                return false;

            // Create new key and persist to Keychain.
            var created = RandomNumberGenerator.GetBytes(KeyBytes);
            var b64 = Convert.ToBase64String(created);

            // NOTE:
            // - We use '-U' to update if exists.
            // - If Keychain prompts or is locked, we time out and fall back to file key.
            var ok = TryRunSecurity(new[]
            {
                "add-generic-password", "-a", Account, "-s", Service, "-w", b64, "-U"
            }, timeout) != null;

            if (!ok)
                return false;

            key = created;
            return true;
        }

        private static string? TryRunSecurity(IReadOnlyList<string> args, TimeSpan timeout)
        {
            try
            {
                using var p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = SecurityBin,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (var a in args)
                    p.StartInfo.ArgumentList.Add(a);

                p.Start();

                var ms = (int)Math.Clamp(timeout.TotalMilliseconds, 50, 30_000);
                if (!p.WaitForExit(ms))
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    return null;
                }

                if (p.ExitCode != 0)
                    return null;

                // Output is small; read after exit.
                return p.StandardOutput.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }
    }

    // ============================================================
    //  Envelope
    // ============================================================

    private sealed class EncryptedEnvelope
    {
        public int SchemaVersion { get; set; }
        public string? Algorithm { get; set; }
        public string? NonceB64 { get; set; }
        public string? TagB64 { get; set; }
        public string? CiphertextB64 { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}


