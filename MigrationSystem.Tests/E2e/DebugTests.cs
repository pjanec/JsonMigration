using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Engine.Internal;
using MigrationSystem.Engine.Public;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace MigrationSystem.Tests.E2e;

public class DebugTests
{
    private readonly ITestOutputHelper _output;

    public DebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Debug_MigrationRegistryTypes()
    {
        var registry = new MigrationRegistry();
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());

        // Try to get the registered types
        try
        {
            var pkgConfV1Type = registry.GetTypeForVersion("PkgConf", "1.0");
            _output.WriteLine($"Found PkgConf 1.0 type: {pkgConfV1Type?.Name}");
        }
        catch (System.Exception ex)
        {
            _output.WriteLine($"Error getting PkgConf 1.0: {ex.Message}");
        }

        try
        {
            var pkgConfV2Type = registry.GetTypeForVersion("PkgConf", "2.0");
            _output.WriteLine($"Found PkgConf 2.0 type: {pkgConfV2Type?.Name}");
        }
        catch (System.Exception ex)
        {
            _output.WriteLine($"Error getting PkgConf 2.0: {ex.Message}");
        }

        // Check if we can find a migration path
        try
        {
            var v1Type = typeof(TestMigrations.PkgConf.PackageConfiguration);
            var v2Type = typeof(TestMigrations.PkgConf.ImprovedPackageConfiguration);
            var path = registry.FindPath(v1Type, v2Type);
            _output.WriteLine($"Migration path found with {path.Count} steps");
        }
        catch (System.Exception ex)
        {
            _output.WriteLine($"Error finding migration path: {ex.Message}");
        }
    }

    [Fact]
    public void Debug_PlanCreation()
    {
        var services = new ServiceCollection();
        services.AddMigrationSystem(options =>
        {
            options.WithMigrationsFromAssembly(typeof(DebugTests).Assembly);
        });
        var serviceProvider = services.BuildServiceProvider();
        var migrationSystem = serviceProvider.GetRequiredService<IMigrationSystem>();

        // Create a simple document bundle
        var metadata = new MetaBlock("PkgConf", "1.0");
        var data = new Newtonsoft.Json.Linq.JObject
        {
            ["timeout"] = 30,
            ["plugins"] = new Newtonsoft.Json.Linq.JArray("auth", "logging")
        };
        
        var doc = new VersionedDocument("test", data, metadata);
        var bundle = new DocumentBundle(doc, System.Linq.Enumerable.Empty<Snapshot>());
        var bundles = new[] { bundle };

        var plan = migrationSystem.OperationalData.PlanUpgradeAsync(bundles).Result;
        
        _output.WriteLine($"Plan created with {plan.Actions.Count} actions");
        if (plan.Actions.Count > 0)
        {
            var action = plan.Actions[0];
            _output.WriteLine($"Action type: {action.ActionType}");
            _output.WriteLine($"Action details: {action.Details}");
        }
    }
}