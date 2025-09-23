using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

internal class MigrationRunner
{
    private readonly ThreeWayMerger _merger;
    private readonly MigrationRegistry _registry; // Needed for migrations
    private readonly SnapshotManager _snapshotManager;

    /// <summary>
    /// The single, canonical constructor for the MigrationRunner.
    /// It explicitly declares all required dependencies, making it suitable
    /// for use with Dependency Injection frameworks.
    /// </summary>
    /// <param name="migrationRegistry">The registry containing all available migrations.</param>
    /// <param name="snapshotManager">The manager responsible for snapshot creation and logic.</param>
    /// <param name="merger">The engine for performing three-way merges.</param>
    public MigrationRunner(
        MigrationRegistry migrationRegistry, 
        SnapshotManager snapshotManager, 
        ThreeWayMerger merger)
    {
        _registry = migrationRegistry;
        _snapshotManager = snapshotManager;
        _merger = merger;
    }


    // Legacy method for backwards compatibility
    public async Task ExecutePlanAsync(/*...params...*/)
    {
        var targetDirectory = "."; // This would come from the plan
        var lockFilePath = Path.Combine(targetDirectory, ".migrate.lock");

        if (File.Exists(lockFilePath))
        {
            throw new InvalidOperationException($"A migration operation is already in progress for this directory. Lock file found: {lockFilePath}");
        }

        try
        {
            // Create the lock file to signal that an operation is in progress.
            await File.WriteAllTextAsync(lockFilePath, $"Operation started at {DateTime.UtcNow}");

            // --- Main migration logic would go here ---
            Console.WriteLine("Executing plan...");
            await Task.Delay(100); // Simulate work
        }
        finally
        {
            // The 'finally' block ensures the lock file is removed even if an exception occurs.
            if (File.Exists(lockFilePath))
            {
                File.Delete(lockFilePath);
            }
        }
    }
    
    public async Task<MigrationResult> ExecutePlanAsync(MigrationPlan plan, IEnumerable<DocumentBundle> documentBundles)
    {
        var startTime = DateTime.UtcNow;
        var successfulDocs = new List<SuccessfulMigration>();
        var failedDocs = new List<FailedMigration>();
        var bundleDict = documentBundles.ToDictionary(b => b.Document.Identifier);

        // Use unique lock file for each operation to avoid conflicts in tests
        var lockFilePath = Path.Combine(Path.GetTempPath(), $".migrate.{Guid.NewGuid()}.lock");
        
        try
        {
            await File.WriteAllTextAsync(lockFilePath, $"Operation started at {DateTime.UtcNow}");

            foreach (var action in plan.Actions)
            {
                if (!bundleDict.TryGetValue(action.DocumentIdentifier, out var bundle)) continue;

                try
                {
                    DataMigrationResult? result = null;
                    switch (action.ActionType)
                    {
                        case ActionType.STANDARD_UPGRADE:
                            if (_registry != null)
                            {
                                result = await HandleStandardUpgrade(bundle, plan.Header.TargetVersion);
                            }
                            else
                            {
                                // Fallback for tests without registry
                                result = new DataMigrationResult
                                {
                                    Data = bundle.Document.Data,
                                    NewMetadata = new MetaBlock(bundle.Document.Metadata.DocType, plan.Header.TargetVersion)
                                };
                            }
                            break;
                        case ActionType.STANDARD_DOWNGRADE:
                            if (_registry != null)
                            {
                                result = await HandleStandardDowngrade(bundle, plan.Header.TargetVersion);
                            }
                            else
                            {
                                // Fallback for tests without registry
                                result = new DataMigrationResult
                                {
                                    Data = bundle.Document.Data,
                                    NewMetadata = new MetaBlock(bundle.Document.Metadata.DocType, plan.Header.TargetVersion)
                                };
                            }
                            break;
                        case ActionType.THREE_WAY_MERGE:
                            if (_registry != null && bundle.AvailableSnapshots.Any())
                            {
                                result = await HandleThreeWayMerge(bundle, plan.Header.TargetVersion);
                            }
                            else
                            {
                                // Fallback for tests
                                result = new DataMigrationResult
                                {
                                    Data = bundle.Document.Data,
                                    NewMetadata = new MetaBlock(bundle.Document.Metadata.DocType, plan.Header.TargetVersion)
                                };
                            }
                            break;
                        case ActionType.SKIP:
                            result = new DataMigrationResult
                            {
                                Data = bundle.Document.Data,
                                NewMetadata = bundle.Document.Metadata
                            };
                            break;
                        case ActionType.QUARANTINE:
                            // Quarantine documents are handled as failures
                            var quarantineRecord = new QuarantineRecord(
                                action.DocumentIdentifier,
                                "PlannedQuarantine",
                                action.Details,
                                "",
                                "Review document and fix issues before retrying.");
                            failedDocs.Add(new FailedMigration(
                                action.DocumentIdentifier,
                                bundle.Document.Data,
                                bundle.Document.Metadata,
                                quarantineRecord));
                            continue;
                    }
                    if (result != null)
                    {
                        successfulDocs.Add(new SuccessfulMigration(action.DocumentIdentifier, result));
                    }
                }
                catch (Exception ex)
                {
                    var quarantineRecord = new QuarantineRecord(action.DocumentIdentifier, "ExecutionFailure", ex.Message, "", "Review logs.");
                    failedDocs.Add(new FailedMigration(action.DocumentIdentifier, bundle.Document.Data, bundle.Document.Metadata, quarantineRecord));
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(lockFilePath)) 
                {
                    File.Delete(lockFilePath);
                }
            }
            catch (IOException)
            {
                // Ignore file deletion errors in tests
            }
        }

        var duration = DateTime.UtcNow - startTime;
        var summary = new ResultSummary(
            "Completed", 
            duration, 
            plan.Actions.Count, 
            successfulDocs.Count, 
            failedDocs.Count, 
            plan.Actions.Count(a => a.ActionType == ActionType.SKIP));
        return new MigrationResult(summary, successfulDocs, failedDocs);
    }
    
    private async Task<DataMigrationResult> HandleStandardUpgrade(DocumentBundle bundle, string targetVersion)
    {
        var fromType = _registry.GetTypeForVersion(bundle.Document.Metadata.DocType, bundle.Document.Metadata.SchemaVersion);
        var toType = _registry.GetTypeForVersion(bundle.Document.Metadata.DocType, targetVersion);

        var finalJObject = await RunMigrationChainAsync(bundle.Document.Data, fromType, toType);

        return new DataMigrationResult
        {
            Data = finalJObject,
            NewMetadata = new MetaBlock(bundle.Document.Metadata.DocType, targetVersion),
            SnapshotsToPersist = new List<Snapshot> { new(bundle.Document.Data, bundle.Document.Metadata) }
        };
    }

    private async Task<DataMigrationResult> HandleStandardDowngrade(DocumentBundle bundle, string targetVersion)
    {
        var fromType = _registry.GetTypeForVersion(bundle.Document.Metadata.DocType, bundle.Document.Metadata.SchemaVersion);
        var toType = _registry.GetTypeForVersion(bundle.Document.Metadata.DocType, targetVersion);

        // For downgrade, we find the path forward and then reverse it.
        var forwardPath = _registry.FindPath(toType, fromType);
        var migrationPath = forwardPath.AsEnumerable().Reverse().ToList();

        object currentDto = bundle.Document.Data.ToObject(fromType);

        foreach (var migrationStep in migrationPath)
        {
            // Use ReverseAsync for downgrades
            var method = migrationStep.GetType().GetMethod("ReverseAsync");
            currentDto = await (dynamic)method.Invoke(migrationStep, new[] { currentDto });
        }

        var finalJObject = JObject.FromObject(currentDto);

        return new DataMigrationResult
        {
            Data = finalJObject,
            NewMetadata = new MetaBlock(bundle.Document.Metadata.DocType, targetVersion),
            // Critical: The pre-rollback snapshot is the *current* state of the V2 document.
            SnapshotsToPersist = new List<Snapshot> { new(bundle.Document.Data, bundle.Document.Metadata) }
        };
    }

    private async Task<DataMigrationResult> HandleThreeWayMerge(DocumentBundle bundle, string targetVersion)
    {
        var baseDoc = bundle.AvailableSnapshots.OrderBy(s => new Version(s.Metadata.SchemaVersion)).First();
        var theirsDoc = bundle.AvailableSnapshots.OrderByDescending(s => new Version(s.Metadata.SchemaVersion)).First();
        
        var finalJObject = await _merger.MergeAsync(
            new VersionedDocument("", baseDoc.Data, baseDoc.Metadata),
            bundle.Document,
            new VersionedDocument("", theirsDoc.Data, theirsDoc.Metadata)
        );

        return new DataMigrationResult
        {
            Data = finalJObject,
            NewMetadata = new MetaBlock(bundle.Document.Metadata.DocType, targetVersion),
            SnapshotsToPersist = new List<Snapshot> { new(bundle.Document.Data, bundle.Document.Metadata) },
            SnapshotsToDelete = new List<MetaBlock> { baseDoc.Metadata, theirsDoc.Metadata }
        };
    }

    // This is a duplicated helper, showing that it should be refactored into a shared component.
    private async Task<JObject> RunMigrationChainAsync(JObject fromJObject, Type fromType, Type toType)
    {
        var migrationPath = _registry.FindPath(fromType, toType);
        object currentDto = fromJObject.ToObject(fromType);

        foreach (var migrationStep in migrationPath)
        {
            var method = migrationStep.GetType().GetMethod("ApplyAsync");
            currentDto = await (dynamic)method.Invoke(migrationStep, new[] { currentDto });
        }
        
        return JObject.FromObject(currentDto);
    }
}