using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json.Linq;

namespace MigrationSystem.Core.Public;

/// <summary>
/// The primary facade for the entire versioning and migration system.
/// This is the main entry point obtained from the MigrationSystemBuilder.
/// </summary>
public interface IMigrationSystem
{
    /// <summary>
    /// Provides high-level APIs for typical application needs, such as
    /// transparently loading and saving single files.
    /// </summary>
    IApplicationApi Application { get; }

    /// <summary>
    /// Provides APIs for processing single, in-memory data objects that are
    /// decoupled from their metadata. Ideal for databases or caches.
    /// </summary>
    IDataApi Data { get; }

    /// <summary>
    /// Provides low-level, file-based APIs for operational tasks like planning
    /// and executing batch migrations. This is a high-level convenience layer.
    /// </summary>
    IOperationalApi Operations { get; }

    /// <summary>
    /// Provides the core, non-file-based operational logic. It knows how to plan
    /// and execute migrations on in-memory collections of documents.
    /// </summary>
    IOperationalDataApi OperationalData { get; }

    /// <summary>
    /// Provides the standalone file discovery service. Its only job is to find
    /// and verify migratable files based on manifest rules.
    /// </summary>
    IDiscoveryService Discovery { get; }
}

/// <summary>
/// High-level APIs for file-based document handling.
/// </summary>
public interface IApplicationApi
{
    Task<LoadResult<T>> LoadLatestAsync<T>(string path, LoadBehavior behavior = LoadBehavior.InMemoryOnly, bool validate = false) where T : class;
    Task SaveLatestAsync<T>(string path, T document) where T : class;
}

/// <summary>
/// APIs for processing in-memory data where the content and metadata are separate.
/// </summary>
public interface IDataApi
{
    Task<T> MigrateToLatestAsync<T>(JObject data, MetaBlock metadata, bool validate = false) where T : class;
    Task<DataMigrationResult> ExecuteUpgradeAsync(JObject data, MetaBlock metadata, IEnumerable<Snapshot>? availableSnapshots = null);
    Task<DataMigrationResult> ExecuteDowngradeAsync(JObject data, MetaBlock metadata, string targetVersion);
}

/// <summary>
/// High-level convenience APIs for file-based batch operations (installers, CLI).
/// </summary>
public interface IOperationalApi
{
    Task<MigrationPlan> PlanUpgradeFromManifestAsync(string? manifestPath = null);
    Task<MigrationPlan> PlanRollbackFromManifestAsync(string targetVersion, string? manifestPath = null);
    Task<MigrationResult> ExecutePlanAgainstFileSystemAsync(MigrationPlan plan);
    Task<MigrationResult> RetryFailedFileSystemAsync(MigrationResult previousResult);
    Task<GcResult> GarbageCollectSnapshotsAsync(string? manifestPath = null);
}

/// <summary>
/// Core operational logic for non-file-based batch migrations.
/// </summary>
public interface IOperationalDataApi
{
    Task<MigrationPlan> PlanUpgradeAsync(IEnumerable<DocumentBundle> documentBundles);
    Task<MigrationPlan> PlanRollbackAsync(IEnumerable<DocumentBundle> documentBundles, string targetVersion);
    Task<MigrationResult> ExecutePlanAsync(MigrationPlan plan, IEnumerable<DocumentBundle> documentBundles);
}

/// <summary>
/// A standalone service for discovering migratable files.
/// </summary>
public interface IDiscoveryService
{
    Task<MigrationManifest> LoadManifestAsync(string manifestPath);
    Task<IEnumerable<string>> DiscoverManagedFilesAsync(MigrationManifest manifest);
}
