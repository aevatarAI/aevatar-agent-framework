using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Aevatar.Agents.Core.Tests;

public sealed class UserSecretsTests
{
    [Fact]
    public void FileAevatarUserSecretsStore_RoundTrip_EncryptsPayload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aevatar-user-secrets-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var options = new AevatarUserSecretsOptions
            {
                SecretsDirectory = dir,
                SecretsFileName = "secrets.json",
                MasterKeyFileName = "masterkey.bin",
                PreferOsKeyStore = false,
                ReloadOnChange = false
            };

            var key = "LLMProviders:Providers:openai:ApiKey";
            var value = "sk-test-123";

            var store = new FileAevatarUserSecretsStore(options);
            store.Set(key, value);

            var store2 = new FileAevatarUserSecretsStore(options);
            store2.TryGet(key, out var loaded).Should().BeTrue();
            loaded.Should().Be(value);

            var secretsPath = Path.Combine(dir, "secrets.json");
            File.Exists(secretsPath).Should().BeTrue();

            var fileText = File.ReadAllText(secretsPath);
            fileText.Should().NotContain(value);
            fileText.Should().Contain("ciphertext", "Should look like an encrypted envelope, not plain JSON.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AddAevatarUserSecrets_LoadsIntoConfiguration()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aevatar-user-secrets-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var store = new FileAevatarUserSecretsStore(new AevatarUserSecretsOptions
            {
                SecretsDirectory = dir,
                SecretsFileName = "secrets.json",
                MasterKeyFileName = "masterkey.bin",
                PreferOsKeyStore = false,
                ReloadOnChange = false
            });

            store.Set("Foo:Bar", "from-secrets");

            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Foo:Bar"] = "from-inmem" })
                .AddAevatarUserSecrets(o =>
                {
                    o.SecretsDirectory = dir;
                    o.SecretsFileName = "secrets.json";
                    o.MasterKeyFileName = "masterkey.bin";
                    o.PreferOsKeyStore = false;
                    o.ReloadOnChange = false;
                })
                .Build();

            cfg["Foo:Bar"].Should().Be("from-secrets");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}


