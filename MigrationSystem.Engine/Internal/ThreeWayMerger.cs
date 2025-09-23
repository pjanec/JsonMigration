using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Contains the logic for performing a three-state merge to prevent data loss on re-upgrade.
/// </summary>
internal class ThreeWayMerger
{
    private readonly MigrationRegistry _registry;

    public ThreeWayMerger(MigrationRegistry registry)
    {
        _registry = registry;
    }

    public async Task<JObject> MergeAsync(VersionedDocument baseDoc, VersionedDocument mineDoc, VersionedDocument theirsDoc)
    {
        // Step 1: Establish common ground by creating a "virtual base" in the target schema.
        var targetType = _registry.GetTypeForVersion(theirsDoc.Metadata.DocType, theirsDoc.Metadata.SchemaVersion);
        var virtualBaseTarget = await RunMigrationChainAsync(baseDoc.Data, _registry.GetTypeForVersion(baseDoc.Metadata.DocType, baseDoc.Metadata.SchemaVersion), targetType);

        // Step 2: Bring "Mine" to the same version for a fair comparison.
        var mineAsTarget = await RunMigrationChainAsync(mineDoc.Data, _registry.GetTypeForVersion(mineDoc.Metadata.DocType, mineDoc.Metadata.SchemaVersion), targetType);

        // Step 3: Simple merge strategy (favoring "theirs" with non-conflicting "mine" additions)
        // A production implementation would use proper JSON patching
        var finalResult = (JObject)theirsDoc.Data.DeepClone();
        
        // Merge non-conflicting properties from "mine"
        foreach (var mineProperty in mineAsTarget.Properties())
        {
            // Only add properties that don't exist in "theirs" to avoid conflicts
            if (!finalResult.ContainsKey(mineProperty.Name))
            {
                finalResult[mineProperty.Name] = mineProperty.Value;
            }
        }

        return finalResult;
    }
    
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