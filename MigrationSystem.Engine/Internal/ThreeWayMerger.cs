using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Contains the logic for performing a three-state merge to prevent data loss on re-upgrade.
/// Enhanced with semantic handler support while maintaining backward compatibility.
/// </summary>
internal class ThreeWayMerger
{
    private readonly MigrationRegistry _registry;

    public ThreeWayMerger(MigrationRegistry registry)
    {
        _registry = registry;
    }

    // Keep the original signature for backward compatibility
    public async Task<JObject> MergeAsync(VersionedDocument baseDoc, VersionedDocument mineDoc, VersionedDocument theirsDoc)
    {
        // Get the migration object to check for semantic handlers
        var targetType = _registry.GetTypeForVersion(theirsDoc.Metadata.DocType, theirsDoc.Metadata.SchemaVersion);
        var baseType = _registry.GetTypeForVersion(baseDoc.Metadata.DocType, baseDoc.Metadata.SchemaVersion);
        var mineType = _registry.GetTypeForVersion(mineDoc.Metadata.DocType, mineDoc.Metadata.SchemaVersion);

        // Get the migration instance for semantic handling
        object? migration = null;
        try
        {
            var migrationPath = _registry.FindPath(baseType, targetType);
            migration = migrationPath.FirstOrDefault();
        }
        catch
        {
            // If no migration found, continue without semantic handling
        }

        // Convert all documents to target schema for fair comparison
        var virtualBaseTarget = await RunMigrationChainAsync(baseDoc.Data, baseType, targetType);
        var mineAsTarget = await RunMigrationChainAsync(mineDoc.Data, mineType, targetType);

        return PerformHybridMerge(virtualBaseTarget, mineAsTarget, theirsDoc.Data, migration);
    }

    private JObject PerformHybridMerge(JObject baseObject, JObject mineObject, JObject theirsObject, object? migration)
    {
        var semanticHandler = migration as ISemanticMigration;
        var handledProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var finalResult = new JObject();

        // --- Stage 1: Identify and process all semantically handled properties ---
        if (semanticHandler != null)
        {
            var allPropertyNames = baseObject.Properties().Select(p => p.Name)
                .Concat(mineObject.Properties().Select(p => p.Name))
                .Concat(theirsObject.Properties().Select(p => p.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var propName in allPropertyNames)
            {
                if (semanticHandler.CanHandleProperty(propName))
                {
                    var baseToken = baseObject[propName];
                    var mineToken = mineObject[propName];
                    var theirsToken = theirsObject[propName];
                    
                    var mergedToken = semanticHandler.MergeProperty(propName, baseToken, mineToken, theirsToken);
                    if (mergedToken != null)
                    {
                        finalResult[propName] = mergedToken;
                    }
                    handledProperties.Add(propName);
                }
            }
        }
        
        // --- Stage 2: Structural merge for all remaining properties ---
        var baseForPatch = new JObject(baseObject.Properties().Where(p => !handledProperties.Contains(p.Name)));
        var mineForPatch = new JObject(mineObject.Properties().Where(p => !handledProperties.Contains(p.Name)));
        var theirsForPatch = new JObject(theirsObject.Properties().Where(p => !handledProperties.Contains(p.Name)));

        var mergedStructural = PerformStructuralMerge(baseForPatch, mineForPatch, theirsForPatch);

        // --- Stage 3: Combine structurally merged properties into the final result ---
        foreach (var prop in mergedStructural.Properties())
        {
            finalResult.Add(prop);
        }

        return finalResult;
    }

    private JObject PerformStructuralMerge(JObject baseObj, JObject mineObj, JObject theirsObj)
    {
        // Start with theirs as base (since we typically prefer their changes)
        var result = (JObject)theirsObj.DeepClone();

        // Analyze changes between base and mine
        var mineChanges = AnalyzeChanges(baseObj, mineObj);
        var theirsChanges = AnalyzeChanges(baseObj, theirsObj);

        // Apply mine's non-conflicting changes
        foreach (var mineChange in mineChanges)
        {
            var propName = mineChange.Key;
            var mineChangeInfo = mineChange.Value;

            // If theirs didn't touch this property, apply mine's change
            if (!theirsChanges.ContainsKey(propName))
            {
                switch (mineChangeInfo.Type)
                {
                    case ChangeType.Removed:
                        result.Remove(propName);
                        break;
                    case ChangeType.Added:
                        result[propName] = mineChangeInfo.NewValue?.DeepClone();
                        break;
                    case ChangeType.Modified:
                        result[propName] = mineChangeInfo.NewValue?.DeepClone();
                        break;
                }
            }
            // For conflicts, theirs wins (default policy for backward compatibility)
        }

        return result;
    }

    private Dictionary<string, ChangeInfo> AnalyzeChanges(JObject baseObj, JObject changedObj)
    {
        var changes = new Dictionary<string, ChangeInfo>();

        // Find removals (properties in base but not in changed)
        foreach (var baseProp in baseObj.Properties())
        {
            if (!changedObj.ContainsKey(baseProp.Name))
            {
                changes[baseProp.Name] = new ChangeInfo 
                { 
                    Type = ChangeType.Removed, 
                    OldValue = baseProp.Value, 
                    NewValue = null 
                };
            }
        }

        // Find additions and modifications
        foreach (var changedProp in changedObj.Properties())
        {
            if (!baseObj.ContainsKey(changedProp.Name))
            {
                changes[changedProp.Name] = new ChangeInfo 
                { 
                    Type = ChangeType.Added, 
                    OldValue = null, 
                    NewValue = changedProp.Value 
                };
            }
            else if (!JToken.DeepEquals(baseObj[changedProp.Name], changedProp.Value))
            {
                changes[changedProp.Name] = new ChangeInfo 
                { 
                    Type = ChangeType.Modified, 
                    OldValue = baseObj[changedProp.Name], 
                    NewValue = changedProp.Value 
                };
            }
        }

        return changes;
    }

    private async Task<JObject> RunMigrationChainAsync(JObject fromJObject, Type fromType, Type toType)
    {
        var migrationPath = _registry.FindPath(fromType, toType);
        object currentDto = fromJObject.ToObject(fromType)!;

        foreach (var migrationStep in migrationPath)
        {
            var method = migrationStep.GetType().GetMethod("ApplyAsync");
            currentDto = await (dynamic)method!.Invoke(migrationStep, new[] { currentDto });
        }

        return JObject.FromObject(currentDto);
    }

    private enum ChangeType
    {
        Added,
        Modified,
        Removed
    }

    private class ChangeInfo
    {
        public ChangeType Type { get; set; }
        public JToken? OldValue { get; set; }
        public JToken? NewValue { get; set; }
    }
}