using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Engine.Internal;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class MigrationPlannerTests
{
    private MigrationPlanner CreateMigrationPlanner()
    {
        var registry = new MigrationRegistry();
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());
        return new MigrationPlanner(registry);
    }

    [Fact]
    public async Task PlanUpgradeAsync_SameVersion_CreatesSkipAction()
    {
        // Arrange - Use PkgConf which has registered migrations
        var planner = CreateMigrationPlanner();
        var document = new VersionedDocument("test-1", new JObject(), new MetaBlock("PkgConf", "2.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        // Act
        var plan = await planner.PlanUpgradeAsync(bundles, "2.0");

        // Assert
        Assert.Single(plan.Actions);
        Assert.Equal(ActionType.SKIP, plan.Actions[0].ActionType);
        Assert.Equal("test-1", plan.Actions[0].DocumentIdentifier);
        Assert.Contains("already at the target version", plan.Actions[0].Details);
    }

    [Fact]
    public async Task PlanUpgradeAsync_NewerVersionSameMajor_CreatesQuarantineAction()
    {
        // Arrange
        var planner = CreateMigrationPlanner();
        var document = new VersionedDocument("test-1", new JObject(), new MetaBlock("PkgConf", "2.5"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        // Act
        var plan = await planner.PlanUpgradeAsync(bundles, "2.0");

        // Assert
        Assert.Single(plan.Actions);
        Assert.Equal(ActionType.QUARANTINE, plan.Actions[0].ActionType);
        Assert.Contains("newer than target", plan.Actions[0].Details);
    }

    [Fact]
    public async Task PlanUpgradeAsync_UnknownDocType_CreatesQuarantineAction()
    {
        // Arrange - Test with unknown doc type (no migrations registered)
        var planner = CreateMigrationPlanner();
        var document = new VersionedDocument("test-1", new JObject(), new MetaBlock("UnknownType", "1.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        // Act
        var plan = await planner.PlanUpgradeAsync(bundles, "2.0");

        // Assert
        Assert.Single(plan.Actions);
        Assert.Equal(ActionType.QUARANTINE, plan.Actions[0].ActionType);
        Assert.Contains("Cannot migrate", plan.Actions[0].Details);
    }

    [Fact]
    public async Task PlanUpgradeAsync_StandardUpgrade_CreatesUpgradeAction()
    {
        // Arrange - Use PkgConf with valid migration path (1.0 -> 2.0)
        var planner = CreateMigrationPlanner();
        var document = new VersionedDocument("test-1", new JObject(), new MetaBlock("PkgConf", "1.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        // Act
        var plan = await planner.PlanUpgradeAsync(bundles, "2.0");

        // Assert
        Assert.Single(plan.Actions);
        Assert.Equal(ActionType.STANDARD_UPGRADE, plan.Actions[0].ActionType);
        Assert.Contains("standard upgrade", plan.Actions[0].Details);
    }

    [Fact]
    public async Task PlanUpgradeAsync_WithRollbackHistory_CreatesThreeWayMergeAction()
    {
        // Arrange - Use PkgConf with rollback history
        var planner = CreateMigrationPlanner();
        var document = new VersionedDocument("test-1", new JObject(), new MetaBlock("PkgConf", "1.0"));
        var snapshot = new Snapshot(new JObject(), new MetaBlock("PkgConf", "2.0")); // Snapshot from higher version
        var bundle = new DocumentBundle(document, new[] { snapshot });
        var bundles = new[] { bundle };

        // Act
        var plan = await planner.PlanUpgradeAsync(bundles, "2.0");

        // Assert
        Assert.Single(plan.Actions);
        Assert.Equal(ActionType.THREE_WAY_MERGE, plan.Actions[0].ActionType);
        Assert.Contains("three-state merge", plan.Actions[0].Details);
    }

    [Fact]
    public async Task PlanRollbackAsync_StandardDowngrade_CreatesDowngradeAction()
    {
        // Arrange - Use PkgConf for rollback
        var planner = CreateMigrationPlanner();
        var document = new VersionedDocument("test-1", new JObject(), new MetaBlock("PkgConf", "2.0"));
        var bundle = new DocumentBundle(document, new List<Snapshot>());
        var bundles = new[] { bundle };

        // Act
        var plan = await planner.PlanRollbackAsync(bundles, "1.0");

        // Assert
        Assert.Single(plan.Actions);
        Assert.Equal(ActionType.STANDARD_DOWNGRADE, plan.Actions[0].ActionType);
        Assert.Contains("standard downgrade", plan.Actions[0].Details);
    }
}