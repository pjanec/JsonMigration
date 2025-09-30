using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Core.Public.Exceptions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Implements the APIs for processing in-memory data.
/// </summary>
internal class DataApi : IDataApi
{
    private readonly MigrationRegistry _registry;
    private readonly SchemaRegistry _schemaRegistry;
    private readonly IOperationalDataApi _operationalDataApi;

    public DataApi(MigrationRegistry registry, SchemaRegistry schemaRegistry, IOperationalDataApi operationalDataApi)
    {
        _registry = registry;
        _schemaRegistry = schemaRegistry;
        _operationalDataApi = operationalDataApi;
    }
    
    // This is a simplified implementation for demonstration.
    // A full implementation would share more logic with the MigrationRunner.
    public async Task<T> MigrateToLatestAsync<T>(JObject data, MetaBlock metadata, bool validate) where T : class
    {
        var fromType = _registry.GetTypeForVersion(metadata.DocType, metadata.SchemaVersion);
        var toType = typeof(T);
        
        // Validation logic (omitted for brevity, but would be similar to ApplicationApi)

        var migrationPath = _registry.FindPath(fromType, toType);
        var currentDto = data.ToObject(fromType) ?? throw new InvalidOperationException("Failed to deserialize data to source type");

        foreach (var migrationStep in migrationPath)
        {
            var method = migrationStep.GetType().GetMethod("ApplyAsync");
            if (method == null)
                throw new InvalidOperationException($"Migration step {migrationStep.GetType().Name} does not have ApplyAsync method");
                
            var dynamicResult = await (dynamic)method.Invoke(migrationStep, new[] { currentDto });
            if (dynamicResult == null)
                throw new InvalidOperationException("Migration step returned null");
            currentDto = dynamicResult!; // null-forgiving operator since we just checked for null
        }
        return (T)currentDto;
    }
    
    public async Task<DataMigrationResult> ExecuteUpgradeAsync(JObject data, MetaBlock metadata, IEnumerable<Snapshot>? availableSnapshots = null)
    {
        var doc = new VersionedDocument("in-memory-doc", data, metadata);
        var bundle = new DocumentBundle(doc, availableSnapshots ?? new List<Snapshot>());
        
        var plan = await _operationalDataApi.PlanUpgradeAsync(new[] { bundle });
        var result = await _operationalDataApi.ExecutePlanAsync(plan, new[] { bundle });

        if (result.FailedDocuments.Any())
        {
            var failure = result.FailedDocuments.First();
            throw new MigrationQuarantineException("Upgrade failed.", failure.QuarantineRecord, failure.OriginalData, failure.OriginalMetadata);
        }
        return result.SuccessfulDocuments.First().Result;
    }

    public async Task<DataMigrationResult> ExecuteDowngradeAsync(JObject data, MetaBlock metadata, string targetVersion)
    {
        var doc = new VersionedDocument("in-memory-doc", data, metadata);
        var bundle = new DocumentBundle(doc, new List<Snapshot>());
        
        var plan = await _operationalDataApi.PlanRollbackAsync(new[] { bundle }, targetVersion);
        var result = await _operationalDataApi.ExecutePlanAsync(plan, new[] { bundle });

        if (result.FailedDocuments.Any())
        {
            var failure = result.FailedDocuments.First();
            throw new MigrationQuarantineException("Downgrade failed.", failure.QuarantineRecord, failure.OriginalData, failure.OriginalMetadata);
        }
        return result.SuccessfulDocuments.First().Result;
    }
}