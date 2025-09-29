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
    private readonly MigrationRegistry _registry;

    public OperationalApi(IDiscoveryService discovery, IOperationalDataApi dataApi, SnapshotManager snapshotManager, QuarantineManager quarantineManager, MigrationRegistry registry)
    {
        _discovery = discovery;
        _dataApi = dataApi;
        _snapshotManager = snapshotManager;
        _quarantineManager = quarantineManager;
        _registry = registry;
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

    // --- NEW SCHEMA CONFIG MANAGEMENT METHODS ---

    public async Task<Dictionary<string, string>> GetLatestSchemaVersionsAsync()
    {
        var versions = new Dictionary<string, string>();
        foreach (var docType in _registry.GetRegisteredDocTypes())
        {
            var latestVersion = _registry.FindLatestVersion(docType);
            if (latestVersion != null)
            {
                versions[docType] = latestVersion;
            }
        }
        return versions;
    }

    public async Task WriteSchemaConfigAsync(string outputFilePath)
    {
        var versions = await GetLatestSchemaVersionsAsync();
        var config = new SchemaConfig(versions);
        // Create a simple wrapper for JSON serialization
        var jsonObject = new { SchemaVersions = config.SchemaVersions };
        var json = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
        await File.WriteAllTextAsync(outputFilePath, json);
    }

    public async Task<MigrationPlan> PlanDowngradeFromConfigAsync(string configPath, string? manifestPath = null)
    {
        // Load the schema config
        var configJson = await File.ReadAllTextAsync(configPath);
        var configData = JsonConvert.DeserializeObject<Dictionary<string, object>>(configJson);
        var schemaVersionsJson = configData?["SchemaVersions"]?.ToString();
        var schemaVersions = JsonConvert.DeserializeObject<Dictionary<string, string>>(schemaVersionsJson ?? "{}");
        var config = new SchemaConfig(schemaVersions ?? new Dictionary<string, string>());

        // Get the document bundles from the manifest
        var bundles = await CreateBundlesFromManifest(manifestPath);
        
        // Use MigrationPlanner to create the plan
        var planner = new MigrationPlanner(_registry);
        return await planner.PlanDowngradeFromConfigAsync(bundles, config);
    }

    // --- ENHANCED EXECUTION WITH TRANSACTION SUPPORT ---

    public async Task<MigrationResult> ExecutePlanAgainstFileSystemAsync(MigrationPlan plan, string? transactionStoragePath = null)
    {
        if (string.IsNullOrEmpty(transactionStoragePath))
        {
            // Execute with simple, non-resumable transaction (existing behavior)
            return await ExecuteSimpleTransactionAsync(plan);
        }

        // --- Resumable Transaction Logic ---

        // Safety check: refuse to start if another transaction is pending
        if (await FindIncompleteMigrationAsync(transactionStoragePath) != null)
        {
            throw new InvalidOperationException("An incomplete migration was found. Please run the resume operation.");
        }

        var transactionId = Guid.NewGuid().ToString();
        var journalPath = Path.Combine(transactionStoragePath, $"journal-{transactionId}.json");
        var backupPath = Path.Combine(transactionStoragePath, $"backup-{transactionId}");
        Directory.CreateDirectory(transactionStoragePath);
        Directory.CreateDirectory(backupPath);

        // 1. Create and write the initial journal
        var operations = plan.Actions
            .Where(a => a.ActionType != ActionType.SKIP)
            .Select(a => new JournalOperation(a.DocumentIdentifier, "Pending"))
            .ToList();
        var journal = new TransactionJournal(transactionId, "InProgress", operations);
        await WriteJournalAsync(journalPath, journal);

        try
        {
            // 2. Backup Phase
            foreach (var op in operations)
            {
                if (File.Exists(op.FilePath))
                {
                    var backupFileName = Path.GetFileName(op.FilePath) + $".{transactionId}.backup";
                    File.Copy(op.FilePath, Path.Combine(backupPath, backupFileName));
                }
            }

            // 3. Execute the plan using the simple transaction method
            var result = await ExecuteSimpleTransactionAsync(plan);

            // 4. Finalize Transaction
            await WriteJournalAsync(journalPath, journal with { Status = "Committed" });

            // Cleanup on success
            Directory.Delete(backupPath, recursive: true);
            File.Delete(journalPath);

            return result;
        }
        catch (Exception)
        {
            // On any failure, the journal is left "InProgress" for the resume operation to handle
            throw;
        }
    }

    public async Task<string?> FindIncompleteMigrationAsync(string transactionStoragePath)
    {
        if (!Directory.Exists(transactionStoragePath)) return null;

        var journalFiles = Directory.EnumerateFiles(transactionStoragePath, "journal-*.json");
        foreach (var journalFile in journalFiles)
        {
            try
            {
                var journalJson = await File.ReadAllTextAsync(journalFile);
                var journal = JsonConvert.DeserializeObject<TransactionJournal>(journalJson);
                if (journal?.Status == "InProgress")
                {
                    return journalFile;
                }
            }
            catch
            {
                // Ignore corrupt journal files
            }
        }
        return null;
    }

    public async Task<MigrationResult> ResumeIncompleteMigrationAsync(string transactionStoragePath)
    {
        var journalPath = await FindIncompleteMigrationAsync(transactionStoragePath);
        if (journalPath == null)
        {
            throw new InvalidOperationException("No incomplete migration found to resume.");
        }

        var journalJson = await File.ReadAllTextAsync(journalPath);
        var journal = JsonConvert.DeserializeObject<TransactionJournal>(journalJson);
        if (journal == null)
        {
            throw new InvalidOperationException("Failed to parse transaction journal.");
        }

        var backupPath = Path.Combine(transactionStoragePath, $"backup-{journal.TransactionId}");
        
        try
        {
            // Restore from backups
            if (Directory.Exists(backupPath))
            {
                foreach (var backupFile in Directory.EnumerateFiles(backupPath, "*.backup"))
                {
                    var originalFileName = Path.GetFileName(backupFile).Replace($".{journal.TransactionId}.backup", "");
                    var originalPath = journal.Operations.FirstOrDefault(op => Path.GetFileName(op.FilePath) == originalFileName)?.FilePath;
                    if (originalPath != null)
                    {
                        File.Copy(backupFile, originalPath, overwrite: true);
                    }
                }
            }

            // Mark as rolled back
            await WriteJournalAsync(journalPath, journal with { Status = "RolledBack" });

            // Cleanup
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, recursive: true);
            }
            File.Delete(journalPath);

            return new MigrationResult(
                new ResultSummary("Rolled Back", TimeSpan.Zero, 0, 0, 0, 0),
                new List<SuccessfulMigration>(),
                new List<FailedMigration>());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to resume migration: {ex.Message}", ex);
        }
    }

    // --- PRIVATE HELPER METHODS ---

    private async Task<MigrationResult> ExecuteSimpleTransactionAsync(MigrationPlan plan)
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

    private async Task WriteJournalAsync(string path, TransactionJournal journal)
    {
        var json = JsonConvert.SerializeObject(journal, Formatting.Indented);
        // Use atomic write for the journal itself
        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
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