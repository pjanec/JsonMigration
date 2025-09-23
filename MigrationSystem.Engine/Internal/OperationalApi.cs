using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

internal class OperationalApi : IOperationalApi
{
    private readonly IDiscoveryService _discovery;
    private readonly IOperationalDataApi _dataApi;
    private readonly SnapshotManager _snapshotManager; // For writing results

    public OperationalApi(IDiscoveryService discovery, IOperationalDataApi dataApi, SnapshotManager snapshotManager)
    {
        _discovery = discovery;
        _dataApi = dataApi;
        _snapshotManager = snapshotManager;
    }

    public async Task<MigrationPlan> PlanUpgradeFromManifestAsync(string? manifestPath = null)
    {
        var bundles = await CreateBundlesFromManifest(manifestPath);
        // A real implementation would determine the latest target version.
        return await _dataApi.PlanUpgradeAsync(bundles);
    }

    public async Task<MigrationPlan> PlanRollbackFromManifestAsync(string targetVersion, string? manifestPath = null)
    {
        var bundles = await CreateBundlesFromManifest(manifestPath);
        return await _dataApi.PlanRollbackAsync(bundles, targetVersion);
    }

    public async Task<MigrationResult> ExecutePlanAgainstFileSystemAsync(MigrationPlan plan)
    {
        // 1. Load all required files and snapshots from disk into memory
        var identifiers = plan.Actions.Select(a => a.DocumentIdentifier).Distinct();
        var bundles = await CreateBundlesFromFilePaths(identifiers);

        // 2. Execute the plan against the in-memory data
        var result = await _dataApi.ExecutePlanAsync(plan, bundles);

        // 3. Commit all changes back to the file system
        foreach (var success in result.SuccessfulDocuments)
        {
            await WriteDataToFile(success.Identifier, success.Result.Data, success.Result.NewMetadata);
            foreach (var snapshot in success.Result.SnapshotsToPersist)
            {
                // Here we assume Identifier is the file path for simplicity
                await _snapshotManager.CreateSnapshotAsync(success.Identifier, snapshot.Data.ToString(Formatting.Indented), snapshot.Metadata.SchemaVersion);
            }
            // ... logic to delete old snapshots ...
        }

        foreach (var failure in result.FailedDocuments)
        {
            // ... logic to write quarantine file ...
        }

        return result;
    }

    public Task<MigrationResult> RetryFailedFileSystemAsync(MigrationResult previousResult) => throw new System.NotImplementedException();
    public Task<GcResult> GarbageCollectSnapshotsAsync(string? manifestPath = null) => throw new System.NotImplementedException();

    private async Task<IEnumerable<DocumentBundle>> CreateBundlesFromManifest(string? manifestPath)
    {
        var manifest = await _discovery.LoadManifestAsync(manifestPath ?? "./MigrationManifest.json");
        var filePaths = await _discovery.DiscoverManagedFilesAsync(manifest);
        return await CreateBundlesFromFilePaths(filePaths);
    }

    private async Task<IEnumerable<DocumentBundle>> CreateBundlesFromFilePaths(IEnumerable<string> filePaths)
    {
        var bundles = new List<DocumentBundle>();
        foreach (var path in filePaths)
        {
             // Simplified: Read file, peek meta, create VersionedDocument.
             // A real implementation would also find and load associated snapshots.
             if (!File.Exists(path)) continue;
             
             var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
             var jobject = JObject.Parse(content);
             var metaToken = jobject["_meta"];
             if (metaToken == null) continue;
             
             var meta = metaToken.ToObject<MetaBlock>();
             if (meta == null) continue;
             
             jobject.Remove("_meta");
             var doc = new VersionedDocument(path, jobject, meta);
             bundles.Add(new DocumentBundle(doc, Enumerable.Empty<Snapshot>()));
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