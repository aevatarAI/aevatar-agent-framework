using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Aevatar.Agents.Persistence.Supabase.GAgent.Setup;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests;

public class SupabaseSchemaScriptOptionTests
{
    [Fact]
    public void BuildStatements_ShouldNotContainRls_WhenDisabledByDefault()
    {
        var statements = SupabaseSchemaScript.BuildStatements(new SupabasePersistenceOptions());

        statements.Should().NotContain(s => s.Contains("ENABLE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));
        statements.Should().NotContain(s => s.Contains("FORCE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStatements_ShouldContainRls_WhenEnabled()
    {
        var options = new SupabasePersistenceOptions
        {
            EnableRowLevelSecurity = true,
            LockDownPublicAccess = false
        };

        var statements = SupabaseSchemaScript.BuildStatements(options);
        statements.Should().Contain(s => s.Contains("ENABLE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStatements_ShouldContainForceRls_WhenEnabled()
    {
        var options = new SupabasePersistenceOptions
        {
            EnableRowLevelSecurity = true,
            ForceRowLevelSecurity = true,
            LockDownPublicAccess = false
        };

        var statements = SupabaseSchemaScript.BuildStatements(options);
        statements.Should().Contain(s => s.Contains("FORCE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStatements_ShouldContainServiceRolePolicies_WhenEnabled()
    {
        var options = new SupabasePersistenceOptions
        {
            EnableRowLevelSecurity = true,
            CreateServiceRolePolicies = true,
            LockDownPublicAccess = false
        };

        var statements = SupabaseSchemaScript.BuildStatements(options);
        statements.Should().Contain(s => s.Contains("CREATE POLICY", StringComparison.OrdinalIgnoreCase));
        statements.Should().Contain(s => s.Contains("service_role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStatements_ShouldSkipLockDown_WhenDisabled()
    {
        var options = new SupabasePersistenceOptions
        {
            LockDownPublicAccess = false
        };

        var statements = SupabaseSchemaScript.BuildStatements(options);

        statements.Should().NotContain(s => s.StartsWith("REVOKE ALL ON SCHEMA", StringComparison.OrdinalIgnoreCase));
        statements.Should().NotContain(s => s.Contains("rolname = 'anon'", StringComparison.OrdinalIgnoreCase));
        statements.Should().NotContain(s => s.Contains("rolname = 'authenticated'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStatements_ShouldRespectIndexToggle()
    {
        var options = new SupabasePersistenceOptions
        {
            AutoCreateIndexes = false
        };

        var statements = SupabaseSchemaScript.BuildStatements(options);
        statements.Should().NotContain(s => s.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildStatements_ShouldNotReturnEmptyStatements()
    {
        var statements = SupabaseSchemaScript.BuildStatements(new SupabasePersistenceOptions());
        statements.Should().NotContain(string.Empty);
        statements.Should().NotContain(s => string.IsNullOrWhiteSpace(s));
    }
}


