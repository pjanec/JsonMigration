using MigrationSystem.Core.Public.DataContracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Analyzes documents to create a detailed, read-only migration plan.
/// </summary>
internal class MigrationPlanner
{
    private readonly MigrationRegistry _registry;

    public MigrationPlanner(MigrationRegistry registry)
    {
        _registry = registry;
    }

    public Task<MigrationPlan> PlanUpgradeAsync(IEnumerable<DocumentBundle> documentBundles, string targetVersion)
    {
        var actions = new List<PlanAction>();
        foreach (var bundle in documentBundles)
        {
            var doc = bundle.Document;
            var currentVersion = new Version(doc.Metadata.SchemaVersion);
            var target = new Version(targetVersion);

            if (currentVersion == target)
            {
                actions.Add(new PlanAction(doc.Identifier, ActionType.SKIP, "Document is already at the target version."));
                continue;
            }

            if (currentVersion > target)
            {
                actions.Add(new PlanAction(doc.Identifier, ActionType.QUARANTINE, $"Document version v{currentVersion} is newer than target v{target}."));
                continue;
            }

            // Check if there's a valid migration path before quarantining
            try
            {
                var fromType = _registry.GetTypeForVersion(doc.Metadata.DocType, doc.Metadata.SchemaVersion);
                var toType = _registry.GetTypeForVersion(doc.Metadata.DocType, targetVersion);
                var migrationPath = _registry.FindPath(fromType, toType);

                // Check for snapshots to determine if a three-way merge is needed.
                var hasRollbackHistory = bundle.AvailableSnapshots
                    .Any(s => new Version(s.Metadata.SchemaVersion).CompareTo(currentVersion) > 0);

                if (hasRollbackHistory)
                {
                    actions.Add(new PlanAction(doc.Identifier, ActionType.THREE_WAY_MERGE, $"Detected snapshots from a previous rollback. A three-state merge will be performed to preserve user data up to v{targetVersion}."));
                }
                else
                {
                    actions.Add(new PlanAction(doc.Identifier, ActionType.STANDARD_UPGRADE, $"A standard upgrade will be performed from v{doc.Metadata.SchemaVersion} to v{targetVersion}."));
                }
            }
            catch (InvalidOperationException ex)
            {
                // No migration path found or type not found - quarantine
                actions.Add(new PlanAction(doc.Identifier, ActionType.QUARANTINE, $"Cannot migrate: {ex.Message}"));
            }
        }

        var header = new PlanHeader(targetVersion, DateTime.UtcNow);
        var plan = new MigrationPlan(header, actions);
        return Task.FromResult(plan);
    }

    public Task<MigrationPlan> PlanRollbackAsync(IEnumerable<DocumentBundle> documentBundles, string targetVersion)
    {
        var actions = new List<PlanAction>();
        foreach (var bundle in documentBundles)
        {
            var doc = bundle.Document;
            var currentVersion = new Version(doc.Metadata.SchemaVersion);
            var target = new Version(targetVersion);

            if (currentVersion == target)
            {
                actions.Add(new PlanAction(doc.Identifier, ActionType.SKIP, "Document is already at the target version."));
                continue;
            }

            if (currentVersion < target)
            {
                actions.Add(new PlanAction(doc.Identifier, ActionType.QUARANTINE, $"Document version v{currentVersion} is older than target v{target}. Use upgrade instead."));
                continue;
            }

            // Check if there's a valid reverse migration path
            try
            {
                var fromType = _registry.GetTypeForVersion(doc.Metadata.DocType, doc.Metadata.SchemaVersion);
                var toType = _registry.GetTypeForVersion(doc.Metadata.DocType, targetVersion);
                
                // For rollback, we need to check if reverse migrations exist
                // For now, we'll assume standard downgrade is possible if types exist
                actions.Add(new PlanAction(doc.Identifier, ActionType.STANDARD_DOWNGRADE, $"A standard downgrade will be performed from v{doc.Metadata.SchemaVersion} to v{targetVersion}."));
            }
            catch (InvalidOperationException ex)
            {
                // No migration path found or type not found - quarantine
                actions.Add(new PlanAction(doc.Identifier, ActionType.QUARANTINE, $"Cannot rollback: {ex.Message}"));
            }
        }

        var header = new PlanHeader(targetVersion, DateTime.UtcNow);
        var plan = new MigrationPlan(header, actions);
        return Task.FromResult(plan);
    }
}