using System.Text.Json;
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
    public void AddAevatarUserConfig_LoadsIntoConfiguration()
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
                .AddAevatarUserConfig(o =>
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

    [Fact]
    public void AddAevatarUserConfig_LoadsBothConfigAndSecrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aevatar-user-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Given: config.json (plaintext)
            var configJson = new
            {
                LLMProviders = new
                {
                    Default = "openai",
                    Providers = new
                    {
                        openai = new
                        {
                            ProviderType = "OpenAI",
                            Model = "gpt-4o"
                        }
                    }
                },
                Logging = new
                {
                    LogLevel = new { Default = "Information" }
                }
            };
            File.WriteAllText(
                Path.Combine(dir, "config.json"),
                JsonSerializer.Serialize(configJson));

            // Given: encrypted secrets.json
            var store = new FileAevatarUserSecretsStore(new AevatarUserSecretsOptions
            {
                SecretsDirectory = dir,
                SecretsFileName = "secrets.json",
                MasterKeyFileName = "masterkey.bin",
                PreferOsKeyStore = false,
                ReloadOnChange = false
            });
            store.Set("LLMProviders:Providers:openai:ApiKey", "sk-secret-key-123");
            store.Set("ServiceApiKeys:GitHub", "ghp_test_token");

            // When: Build configuration with AddAevatarUserConfig
            var cfg = new ConfigurationBuilder()
                .AddAevatarUserConfig(o =>
                {
                    o.SecretsDirectory = dir;
                    o.SecretsFileName = "secrets.json";
                    o.ConfigFileName = "config.json";
                    o.MasterKeyFileName = "masterkey.bin";
                    o.PreferOsKeyStore = false;
                    o.ReloadOnChange = false;
                })
                .Build();

            // Then: config.json values are loaded
            cfg["LLMProviders:Default"].Should().Be("openai");
            cfg["LLMProviders:Providers:openai:ProviderType"].Should().Be("OpenAI");
            cfg["LLMProviders:Providers:openai:Model"].Should().Be("gpt-4o");
            cfg["Logging:LogLevel:Default"].Should().Be("Information");

            // Then: secrets.json values are loaded
            cfg["LLMProviders:Providers:openai:ApiKey"].Should().Be("sk-secret-key-123");
            cfg["ServiceApiKeys:GitHub"].Should().Be("ghp_test_token");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AddAevatarUserConfig_SecretsOverrideConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aevatar-user-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Given: config.json with a value that will be overridden
            var configJson = new
            {
                LLMProviders = new
                {
                    Providers = new
                    {
                        openai = new
                        {
                            ApiKey = "placeholder-from-config"
                        }
                    }
                },
                SharedSetting = "from-config"
            };
            File.WriteAllText(
                Path.Combine(dir, "config.json"),
                JsonSerializer.Serialize(configJson));

            // Given: secrets.json with the same key (should override)
            var store = new FileAevatarUserSecretsStore(new AevatarUserSecretsOptions
            {
                SecretsDirectory = dir,
                SecretsFileName = "secrets.json",
                MasterKeyFileName = "masterkey.bin",
                PreferOsKeyStore = false,
                ReloadOnChange = false
            });
            store.Set("LLMProviders:Providers:openai:ApiKey", "real-secret-from-secrets");
            store.Set("SharedSetting", "from-secrets");

            // When
            var cfg = new ConfigurationBuilder()
                .AddAevatarUserConfig(o =>
                {
                    o.SecretsDirectory = dir;
                    o.SecretsFileName = "secrets.json";
                    o.ConfigFileName = "config.json";
                    o.MasterKeyFileName = "masterkey.bin";
                    o.PreferOsKeyStore = false;
                    o.ReloadOnChange = false;
                })
                .Build();

            // Then: secrets.json should override config.json
            cfg["LLMProviders:Providers:openai:ApiKey"].Should().Be("real-secret-from-secrets");
            cfg["SharedSetting"].Should().Be("from-secrets");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AddAevatarUserConfig_WorksWithMissingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aevatar-user-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Given: no config or secrets files exist
            // When
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Fallback:Key"] = "fallback-value" })
                .AddAevatarUserConfig(o =>
                {
                    o.SecretsDirectory = dir;
                    o.SecretsFileName = "secrets.json";
                    o.ConfigFileName = "config.json";
                    o.MasterKeyFileName = "masterkey.bin";
                    o.PreferOsKeyStore = false;
                    o.ReloadOnChange = false;
                })
                .Build();

            // Then: should not throw, fallback should work
            cfg["Fallback:Key"].Should().Be("fallback-value");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FlattenJson_HandlesNestedObjects()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aevatar-user-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Given: deeply nested config.json
            var configJson = new
            {
                Level1 = new
                {
                    Level2 = new
                    {
                        Level3 = new
                        {
                            Value = "deep-value"
                        }
                    }
                }
            };
            File.WriteAllText(
                Path.Combine(dir, "config.json"),
                JsonSerializer.Serialize(configJson));

            // When
            var cfg = new ConfigurationBuilder()
                .AddAevatarUserConfig(o =>
                {
                    o.SecretsDirectory = dir;
                    o.ConfigFileName = "config.json";
                    o.SecretsFileName = "secrets.json";
                    o.MasterKeyFileName = "masterkey.bin";
                    o.PreferOsKeyStore = false;
                    o.ReloadOnChange = false;
                })
                .Build();

            // Then
            cfg["Level1:Level2:Level3:Value"].Should().Be("deep-value");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FlattenJson_HandlesArrays()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aevatar-user-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Given: config.json with arrays
            var configJson = new
            {
                Items = new[] { "first", "second", "third" },
                Nested = new
                {
                    Values = new[] { 1, 2, 3 }
                }
            };
            File.WriteAllText(
                Path.Combine(dir, "config.json"),
                JsonSerializer.Serialize(configJson));

            // When
            var cfg = new ConfigurationBuilder()
                .AddAevatarUserConfig(o =>
                {
                    o.SecretsDirectory = dir;
                    o.ConfigFileName = "config.json";
                    o.SecretsFileName = "secrets.json";
                    o.MasterKeyFileName = "masterkey.bin";
                    o.PreferOsKeyStore = false;
                    o.ReloadOnChange = false;
                })
                .Build();

            // Then
            cfg["Items:0"].Should().Be("first");
            cfg["Items:1"].Should().Be("second");
            cfg["Items:2"].Should().Be("third");
            cfg["Nested:Values:0"].Should().Be("1");
            cfg["Nested:Values:1"].Should().Be("2");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AddAevatarUserConfig_WithMongoEnvVar_DoesNotThrow()
    {
        var envVar = "AEVATAR_MONGODB_CONNECTION_STRING";
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, "mongodb://localhost:27017/test_db");

        try
        {
            var builder = new ConfigurationBuilder();
            var act = () => builder.AddAevatarUserConfig();
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    [Fact]
    public void AddAevatarUserConfig_WithExplicitMongoConfig_DoesNotThrow()
    {
        var builder = new ConfigurationBuilder();
        var act = () => builder.AddAevatarUserConfig(o => 
        {
            o.MongoConnectionString = "mongodb://localhost:27017/test_db";
        });
        act.Should().NotThrow();
    }
}


