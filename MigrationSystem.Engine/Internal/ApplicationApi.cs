using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Core.Public.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
            // Fallback for legacy files without _meta blocks:
            // Try to infer DocType from the target type T's SchemaVersion attribute
            var schemaAttr = typeof(T).GetCustomAttribute<SchemaVersionAttribute>();
            if (schemaAttr != null)
            {
                // Use the DocType from the target type and assume version 1.0
                meta = new MetaBlock(schemaAttr.DocType, "1.0");
            }
            else
            {
                // Last resort: use the type name as DocType and version 1.0
                // This maintains backward compatibility with existing code
                meta = new MetaBlock(typeof(T).Name, "1.0");
            }
        }
        else
        {
            // If a _meta block exists, parse it as usual.
            meta = metaToken.ToObject<MetaBlock>()!;
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
        
        object currentDto = jobject.ToObject(fromType)!;

        // Execute the migration chain
        foreach (var migrationStep in migrationPath)
        {
            var fromDtoType = migrationStep.GetType().GetInterfaces().First().GetGenericArguments()[0];
            var toDtoType = migrationStep.GetType().GetInterfaces().First().GetGenericArguments()[1];
            
            var method = migrationStep.GetType().GetMethod("ApplyAsync");
            currentDto = await (dynamic)method!.Invoke(migrationStep, new[] { currentDto });
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
        // 1. Get the SchemaVersion attribute from the DTO type.
        var schemaAttr = typeof(T).GetCustomAttribute<SchemaVersionAttribute>();
        if (schemaAttr == null)
        {
            throw new InvalidOperationException($"Type '{typeof(T).FullName}' is not a versioned DTO. Did you forget to add the [SchemaVersion] attribute?");
        }
        var docType = schemaAttr.DocType;
    
        // 2. Find the latest registered version for this DTO's DocType.
        var latestVersion = _registry.FindLatestVersion(docType);

        if (latestVersion == null)
        {
            throw new InvalidOperationException($"No versions are registered for DocType '{docType}'. Cannot determine the latest version to save.");
        }

        // 3. Create the meta block for the latest version.
        var meta = new MetaBlock(docType, latestVersion);

        // 4. Serialize the document and add the meta block.
        var jobject = JObject.FromObject(document);
        jobject["_meta"] = JObject.FromObject(meta);
        var json = jobject.ToString(Formatting.Indented);
    
        // 5. Perform an atomic write to the file system.
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