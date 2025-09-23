using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Engine.Internal;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class OperationalDataApiTests
{
    private OperationalDataApi CreateOperationalDataApi()
    {
        var registry = new MigrationRegistry();
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());
        
        var planner = new MigrationPlanner(registry);
        var merger = new ThreeWayMerger(registry);
        var snapshotManager = new SnapshotManager();
        var runner = new MigrationRunner(planner, merger, snapshotManager);
        
        return new OperationalDataApi(planner, runner);
    }

    [Fact]
    public async Task PlanUpgradeAsync_ReturnsPlan()
    {
        // Arrange
        var api = CreateOperationalDataApi();
        var document = new VersionedDocument("test-1", new JObject { ["name"] = "test" }, new MetaBlock("Test", "1.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        // Act
        var plan = await api.PlanUpgradeAsync(bundles);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("2.0", plan.Header.TargetVersion);
        Assert.Single(plan.Actions);
    }

    [Fact]
    public async Task PlanRollbackAsync_ReturnsPlan()
    {
        // Arrange
        var api = CreateOperationalDataApi();
        var document = new VersionedDocument("test-1", new JObject { ["name"] = "test" }, new MetaBlock("Test", "2.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        // Act
        var plan = await api.PlanRollbackAsync(bundles, "1.5");

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("1.5", plan.Header.TargetVersion);
        Assert.Single(plan.Actions);
    }

    [Fact]
    public async Task ExecutePlanAsync_ReturnsResult()
    {
        // Arrange
        var api = CreateOperationalDataApi();
        var document = new VersionedDocument("test-1", new JObject { ["name"] = "test" }, new MetaBlock("Test", "1.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        var plan = new MigrationPlan(
            new PlanHeader("1.0", DateTime.UtcNow),
            new[] { new PlanAction("test-1", ActionType.SKIP, "Already at target") });

        // Act
        var result = await api.ExecutePlanAsync(plan, bundles);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Completed", result.Summary.Status);
        Assert.Equal(1, result.Summary.Processed);
    }
}