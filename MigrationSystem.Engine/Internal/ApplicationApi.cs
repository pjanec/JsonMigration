using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Core.Public.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Implements the high-level APIs for file-based document handling.
/// </summary>
internal class ApplicationApi : IApplicationApi
{
    private readonly MigrationRegistry _registry;
    private readonly SchemaRegistry _schemaRegistry;
    private readonly SnapshotManager _snapshotManager;

    public ApplicationApi(MigrationRegistry registry, SchemaRegistry schemaRegistry, SnapshotManager snapshotManager)
    {
        _registry = registry;
        _schemaRegistry = schemaRegistry;
        _snapshotManager = snapshotManager;
    }

    public async Task<LoadResult<T>> LoadLatestAsync<T>(string path, LoadBehavior behavior, bool validate) where T : class
    {
        var jsonContent = await File.ReadAllTextAsync(path, Encoding.UTF8);
        var jobject = JObject.Parse(jsonContent);
        
        var metaToken = jobject["_meta"];
        if (metaToken == null)
        {
            // Handle files without metadata
            throw new InvalidOperationException("File does not contain a _meta block and cannot be migrated.");
        }

        var meta = metaToken.ToObject<MetaBlock>();
        var fromType = _registry.GetTypeForVersion(meta.DocType, meta.SchemaVersion);

        if (validate)
        {
            var schema = _schemaRegistry.GetSchema(fromType);
            var errors = schema.Validate(jobject);
            if (errors.Any())
            {
                var quarantineRecord = new QuarantineRecord(path, "SchemaValidationFailure", string.Join("; ", errors.Select(e => e.ToString())), "", "Fix data to conform to schema.");
                // This would be a MigrationQuarantineException in the DataApi
                throw new Exception($"Schema validation failed: {quarantineRecord.Details}");
            }
        }

        var migrationPath = _registry.FindPath(fromType, typeof(T));
        var wasMigrated = migrationPath.Any();
        
        object currentDto = jobject.ToObject(fromType);

        // Execute the migration chain
        foreach (var migrationStep in migrationPath)
        {
            var fromDtoType = migrationStep.GetType().GetInterfaces().First().GetGenericArguments()[0];
            var toDtoType = migrationStep.GetType().GetInterfaces().First().GetGenericArguments()[1];
            
            var method = migrationStep.GetType().GetMethod("ApplyAsync");
            currentDto = await (dynamic)method.Invoke(migrationStep, new[] { currentDto });
        }
        
        if (wasMigrated && behavior == LoadBehavior.MigrateOnDisk)
        {
            // This is a state-changing operation, so we create a snapshot first
            await _snapshotManager.CreateSnapshotAsync(path, jsonContent, meta.SchemaVersion);
            await SaveLatestAsync(path, (T)currentDto);
        }

        return new LoadResult<T>((T)currentDto, wasMigrated);
    }

    public async Task SaveLatestAsync<T>(string path, T document) where T : class
    {
        // This is a simplified Save. A full implementation would dynamically
        // determine the latest version and attach the correct MetaBlock.
        var json = JsonConvert.SerializeObject(document, Formatting.Indented);
        
        var tempFilePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFilePath, json, Encoding.UTF8);
        File.Move(tempFilePath, path, overwrite: true);
    }
}