using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Engine.Internal;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class MigrationRunnerTests
{
    private MigrationRunner CreateMigrationRunner()
    {
        var registry = new MigrationRegistry();
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());
        
        var merger = new ThreeWayMerger(registry);
        var snapshotManager = new SnapshotManager();
        
        return new MigrationRunner(registry, snapshotManager, merger);
    }

    [Fact]
    public async Task ExecutePlanAsync_SkipAction_ReturnsSuccessfulResult()
    {
        // Arrange
        var runner = CreateMigrationRunner();
        var document = new VersionedDocument("test-1", 
            new JObject { ["timeout"] = 30, ["plugins"] = new JArray("auth") }, 
            new MetaBlock("PkgConf", "1.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        var plan = new MigrationPlan(
            new PlanHeader("1.0", DateTime.UtcNow),
            new[] { new PlanAction("test-1", ActionType.SKIP, "Already at target version") });

        // Act
        var result = await runner.ExecutePlanAsync(plan, bundles);

        // Assert
        Assert.Equal("Completed", result.Summary.Status);
        Assert.Equal(1, result.Summary.Processed);
        Assert.Equal(1, result.Summary.Succeeded);
        Assert.Equal(0, result.Summary.Failed);
        Assert.Single(result.SuccessfulDocuments);
        Assert.Empty(result.FailedDocuments);
    }

    [Fact]
    public async Task ExecutePlanAsync_QuarantineAction_ReturnsFailedResult()
    {
        // Arrange
        var runner = CreateMigrationRunner();
        var document = new VersionedDocument("test-1", 
            new JObject { ["timeout"] = 30, ["plugins"] = new JArray("auth") }, 
            new MetaBlock("PkgConf", "1.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        var plan = new MigrationPlan(
            new PlanHeader("2.0", DateTime.UtcNow),
            new[] { new PlanAction("test-1", ActionType.QUARANTINE, "Major version mismatch") });

        // Act
        var result = await runner.ExecutePlanAsync(plan, bundles);

        // Assert
        Assert.Equal("Completed", result.Summary.Status);
        Assert.Equal(1, result.Summary.Processed);
        Assert.Equal(0, result.Summary.Succeeded);
        Assert.Equal(1, result.Summary.Failed);
        Assert.Empty(result.SuccessfulDocuments);
        Assert.Single(result.FailedDocuments);
        Assert.Equal("PlannedQuarantine", result.FailedDocuments[0].QuarantineRecord.Reason);
    }

    [Fact]
    public async Task ExecutePlanAsync_StandardUpgrade_ReturnsSuccessfulResult()
    {
        // Arrange
        var runner = CreateMigrationRunner();
        var document = new VersionedDocument("test-1", 
            new JObject { ["timeout"] = 30, ["plugins"] = new JArray("auth", "logging") }, 
            new MetaBlock("PkgConf", "1.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        var plan = new MigrationPlan(
            new PlanHeader("2.0", DateTime.UtcNow),
            new[] { new PlanAction("test-1", ActionType.STANDARD_UPGRADE, "Upgrade from 1.0 to 2.0") });

        // Act
        var result = await runner.ExecutePlanAsync(plan, bundles);

        // Assert
        Assert.Equal("Completed", result.Summary.Status);
        Assert.Equal(1, result.Summary.Processed);
        Assert.Equal(1, result.Summary.Succeeded);
        Assert.Equal(0, result.Summary.Failed);
        Assert.Single(result.SuccessfulDocuments);
        Assert.Equal("2.0", result.SuccessfulDocuments[0].Result.NewMetadata.SchemaVersion);
    }
}
