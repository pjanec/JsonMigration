using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;

namespace MigrationSystem.Engine.Internal;

internal class OperationalApi : IOperationalApi
{
    private readonly IDiscoveryService _discovery;
    private readonly IOperationalDataApi _dataApi;
    private readonly SnapshotManager _snapshotManager;
    private readonly QuarantineManager _quarantineManager;

    public OperationalApi(IDiscoveryService discovery, IOperationalDataApi dataApi, SnapshotManager snapshotManager, QuarantineManager quarantineManager)
    {
        _discovery = discovery;
        _dataApi = dataApi;
        _snapshotManager = snapshotManager;
        _quarantineManager = quarantineManager;
    }

    public async Task<MigrationPlan> PlanUpgradeFromManifestAsync(string? manifestPath = null)
    {
        var bundles = await CreateBundlesFromManifest(manifestPath);
        return await _dataApi.PlanUpgradeAsync(bundles);
    }
    
    public async Task<MigrationPlan> PlanRollbackFromManifestAsync(string targetVersion, string? manifestPath = null)
    {
        var bundles = await CreateBundlesFromManifest(manifestPath);
        return await _dataApi.PlanRollbackAsync(bundles, targetVersion);
    }

    public async Task<MigrationResult> ExecutePlanAgainstFileSystemAsync(MigrationPlan plan)
    {
        var identifiers = plan.Actions.Select(a => a.DocumentIdentifier).Distinct();
        var preExecutionFailures = new List<FailedMigration>();
        
        // This will now populate preExecutionFailures instead of throwing
        var bundles = await CreateBundlesFromFilePaths(identifiers, preExecutionFailures);

        var result = await _dataApi.ExecutePlanAsync(plan, bundles);

        // Combine failures from loading with failures from execution
        var allFailed = result.FailedDocuments.ToList();
        allFailed.AddRange(preExecutionFailures);
        
        var updatedSummary = new ResultSummary(
            result.Summary.Status,
            result.Summary.Duration,
            result.Summary.Processed,
            result.Summary.Succeeded,
            allFailed.Count,
            result.Summary.Skipped);

        var finalResult = new MigrationResult(updatedSummary, result.SuccessfulDocuments, allFailed);

        await CommitResultsToDisk(finalResult);
        return finalResult;
    }

    public async Task<MigrationResult> RetryFailedFileSystemAsync(MigrationResult previousResult)
    {
        var failedIdentifiers = previousResult.FailedDocuments.Select(f => f.Identifier);
        if (!failedIdentifiers.Any())
        {
            return previousResult; // Nothing to retry
        }
        
        var preExecutionFailures = new List<FailedMigration>();
        var bundles = await CreateBundlesFromFilePaths(failedIdentifiers, preExecutionFailures);
        
        // Create a new plan containing only the failed items, inferring the action.
        var retryActions = failedIdentifiers.Select(id => 
            new PlanAction(id, ActionType.STANDARD_UPGRADE, "Retry migration")).ToList();
        
        var retryPlan = new MigrationPlan(
            new PlanHeader("latest", DateTime.UtcNow),
            retryActions
        );
        
        var result = await _dataApi.ExecutePlanAsync(retryPlan, bundles);
        
        // Combine failures from loading with failures from execution
        var allFailed = result.FailedDocuments.ToList();
        allFailed.AddRange(preExecutionFailures);
        
        var updatedSummary = new ResultSummary(
            result.Summary.Status,
            result.Summary.Duration,
            result.Summary.Processed,
            result.Summary.Succeeded,
            allFailed.Count,
            result.Summary.Skipped);

        var finalResult = new MigrationResult(updatedSummary, result.SuccessfulDocuments, allFailed);
        
        await CommitResultsToDisk(finalResult);
        return finalResult;
    }

    public async Task<GcResult> GarbageCollectSnapshotsAsync(string? manifestPath = null)
    {
        var bundles = await CreateBundlesFromManifest(manifestPath);
        foreach(var bundle in bundles)
        {
            var liveVersion = new Version(bundle.Document.Metadata.SchemaVersion);
            foreach(var snapshot in bundle.AvailableSnapshots)
            {
                var snapshotVersion = new Version(snapshot.Metadata.SchemaVersion);
                if(snapshotVersion <= liveVersion)
                {
                    // Find the snapshot file on disk and delete it
                    var directory = Path.GetDirectoryName(bundle.Document.Identifier) ?? ".";
                    var fileName = Path.GetFileName(bundle.Document.Identifier);
                    var snapshotPattern = $"{fileName}.v{snapshot.Metadata.SchemaVersion}.*.snapshot.json";
                    var filesToDelete = Directory.EnumerateFiles(directory, snapshotPattern);
                    foreach (var file in filesToDelete)
                    {
                        File.Delete(file);
                        Console.WriteLine($"GC: Deleted obsolete snapshot: {file}");
                    }
                }
            }
        }
        return new GcResult();
    }
    
    private async Task CommitResultsToDisk(MigrationResult result)
    {
        foreach (var success in result.SuccessfulDocuments)
        {
            // The core engine in `DataMigrationResult` now correctly calculates
            // which snapshots to persist for both upgrades and downgrades.
            // This loop now correctly handles all cases.
            await WriteDataToFile(success.Identifier, success.Result.Data, success.Result.NewMetadata);
            foreach (var snapshot in success.Result.SnapshotsToPersist)
            {
                var fullSnapshotContent = new JObject(snapshot.Data);
                fullSnapshotContent["_meta"] = JObject.FromObject(snapshot.Metadata);
                await _snapshotManager.CreateSnapshotAsync(success.Identifier, fullSnapshotContent.ToString(Formatting.Indented), snapshot.Metadata.SchemaVersion);
            }
            foreach (var metaToDelete in success.Result.SnapshotsToDelete)
            {
                // Find and delete the now-obsolete snapshot file
                var directory = Path.GetDirectoryName(success.Identifier) ?? ".";
                var fileName = Path.GetFileName(success.Identifier);
                var snapshotPattern = $"{fileName}.v{metaToDelete.SchemaVersion}.*.snapshot.json";
                var filesToDelete = Directory.EnumerateFiles(directory, snapshotPattern);
                foreach (var file in filesToDelete)
                {
                    File.Delete(file);
                }
            }
        }
        foreach (var failure in result.FailedDocuments)
        {
            if (File.Exists(failure.Identifier))
            {
                await _quarantineManager.QuarantineFileAsync(failure.Identifier, failure.QuarantineRecord);
            }
        }
    }

    private async Task<IEnumerable<DocumentBundle>> CreateBundlesFromManifest(string? manifestPath)
    {
        // Use the override path when loading the manifest
        var manifest = await _discovery.LoadManifestAsync(manifestPath);
        var filePaths = await _discovery.DiscoverManagedFilesAsync(manifest);
        var failures = new List<FailedMigration>(); // Ignored for planning phase
        return await CreateBundlesFromFilePaths(filePaths, failures);
    }

    private async Task<List<DocumentBundle>> CreateBundlesFromFilePaths(IEnumerable<string> filePaths, List<FailedMigration> outFailures)
    {
        var bundles = new List<DocumentBundle>();
        foreach (var path in filePaths)
        {
             if (!File.Exists(path)) continue;
             
             try
             {
                 var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
                 var jobject = JObject.Parse(content);
                 var metaToken = jobject["_meta"];
                 if (metaToken == null) continue;
                 
                 var meta = metaToken.ToObject<MetaBlock>();
                 if (meta == null) continue;
                 
                 jobject.Remove("_meta");
                 var doc = new VersionedDocument(path, jobject, meta);

                 var availableSnapshots = new List<Snapshot>();
                 var directory = Path.GetDirectoryName(path) ?? ".";
                 var fileName = Path.GetFileName(path);
                 var snapshotPattern = $"{fileName}.v*.*.snapshot.json";
                 
                 foreach (var snapshotFile in Directory.EnumerateFiles(directory, snapshotPattern))
                 {
                     // Read AND verify the snapshot's integrity
                     var snapshotContent = await _snapshotManager.ReadAndVerifySnapshotAsync(snapshotFile);
                     var snapshotJObject = JObject.Parse(snapshotContent);
                     var snapshotMeta = snapshotJObject["_meta"]?.ToObject<MetaBlock>();
                     if (snapshotMeta == null) continue;
                     snapshotJObject.Remove("_meta");
                     availableSnapshots.Add(new Snapshot(snapshotJObject, snapshotMeta));
                 }

                 bundles.Add(new DocumentBundle(doc, availableSnapshots));
             }
             catch (SnapshotIntegrityException ex)
             {
                 var quarantineRecord = new QuarantineRecord(path, "SnapshotIntegrityFailure", ex.Message, "", "Delete corrupt snapshot and restore from backup.");
                 outFailures.Add(new FailedMigration(path, new JObject(), new MetaBlock("Unknown", "0.0"), quarantineRecord));
             }
             catch (IOException ex)
             {
                var quarantineRecord = new QuarantineRecord(path, "ExecutionFailure", ex.Message, "", "Unlock the file and retry.");
                outFailures.Add(new FailedMigration(path, new JObject(), new MetaBlock("Unknown", "0.0"), quarantineRecord));
             }
        }
        return bundles;
    }

    private async Task WriteDataToFile(string path, JObject data, MetaBlock metadata)
    {
        var fullObject = new JObject(data);
        fullObject["_meta"] = JObject.FromObject(metadata);
        var content = fullObject.ToString(Formatting.Indented);
        
        var tempFilePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFilePath, content, Encoding.UTF8);
        File.Move(tempFilePath, path, overwrite: true);
    }
}