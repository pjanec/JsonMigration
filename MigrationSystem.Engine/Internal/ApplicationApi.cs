using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Core.Public.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    private readonly QuarantineManager _quarantineManager;

    public ApplicationApi(MigrationRegistry registry, SchemaRegistry schemaRegistry, SnapshotManager snapshotManager, QuarantineManager quarantineManager)
    {
        _registry = registry;
        _schemaRegistry = schemaRegistry;
        _snapshotManager = snapshotManager;
        _quarantineManager = quarantineManager;
    }

    public async Task<LoadResult<T>> LoadLatestAsync<T>(string path, LoadBehavior behavior, bool validate) where T : class
    {
        var jsonContent = await File.ReadAllTextAsync(path, Encoding.UTF8);
        var jobject = JObject.Parse(jsonContent);
        
        var metaToken = jobject["_meta"];
        
        MetaBlock meta;
        if (metaToken == null)
        {
            // If no _meta block is found, treat the file as version 1.0.
            // The document type is inferred from the generic type T.
            meta = new MetaBlock(typeof(T).Name, "1.0");
        }
        else
        {
            // If a _meta block exists, parse it as usual.
            meta = metaToken.ToObject<MetaBlock>();
        }

        var fromType = _registry.GetTypeForVersion(meta.DocType, meta.SchemaVersion);

        if (validate)
        {
            var schema = _schemaRegistry.GetSchema(fromType);
            var errors = schema.Validate(jobject);
            if (errors.Any())
            {
                // Validation failed. Time to quarantine.
                var contentHash = ComputeSha256(jsonContent);
                var details = string.Join("; ", errors.Select(e => e.ToString()));
                var quarantineRecord = new QuarantineRecord(path, "SchemaValidationFailure", details, contentHash, "Fix data to conform to schema.");
                
                // This is a fire-and-forget call for simplicity. A more robust system
                // might await this and include the report path in the exception.
                _ = _quarantineManager.QuarantineFileAsync(path, quarantineRecord);

                // Throw a specific exception
                throw new MigrationQuarantineException($"Schema validation failed: {details}", quarantineRecord, jobject, meta);
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

    // Add a helper method to compute the hash for the quarantine record
    private string ComputeSha256(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}