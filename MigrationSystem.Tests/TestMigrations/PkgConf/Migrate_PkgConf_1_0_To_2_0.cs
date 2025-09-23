using MigrationSystem.Core.Contracts;
using MigrationSystem.Core.Public;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MigrationSystem.Tests.TestMigrations.PkgConf;

/// <summary>
/// This migration now implements ISemanticMigration to provide custom merge logic
/// for the 'plugins' property, which transforms from a List to a Dictionary.
/// </summary>
public class Migrate_PkgConf_1_0_To_2_0 : IJsonMigration<PkgConfV1_0, PkgConfV2_0>, ISemanticMigration
{
    public Task<PkgConfV2_0> ApplyAsync(PkgConfV1_0 fromDto)
    {
        var toDto = new PkgConfV2_0
        {
            Execution_timeout = fromDto.Timeout,
            Plugins = fromDto.Plugins.ToDictionary(
                p => p,
                p => new PkgConfV2_0_Plugin { Enabled = true }
            ),
            Reporting = new PkgConfV2_0_Reporting { Format = "json" }
        };
        return Task.FromResult(toDto);
    }

    public Task<PkgConfV1_0> ReverseAsync(PkgConfV2_0 toDto)
    {
        var fromDto = new PkgConfV1_0
        {
            Timeout = toDto.Execution_timeout,
            Plugins = toDto.Plugins.Keys.ToList()
        };
        return Task.FromResult(fromDto);
    }

    // --- Semantic Handler Implementation ---
    public bool CanHandleProperty(string propertyName)
    {
        // Only provide custom logic for the 'plugins' property.
        return propertyName.Equals("Plugins", StringComparison.OrdinalIgnoreCase);
    }

    public JToken MergeProperty(string propertyName, JToken? baseToken, JToken? mineToken, JToken? theirsToken)
    {
        try
        {
            if (!propertyName.Equals("Plugins", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"This handler only supports 'Plugins' property, got '{propertyName}'");
            }

            // Start with theirs as the base result (it has the V2 dictionary format)
            var finalPlugins = (JObject)(theirsToken?.DeepClone() ?? new JObject());

            // Get the original plugin names from base and mine (V1 format - arrays)
            var basePluginNames = new HashSet<string>();
            var minePluginNames = new HashSet<string>();

            // Safe conversion with null checks
            if (baseToken != null && baseToken.Type == JTokenType.Array)
            {
                basePluginNames = new HashSet<string>(baseToken.ToObject<List<string>>() ?? new List<string>());
            }
            else if (baseToken != null && baseToken.Type == JTokenType.Object)
            {
                // If base is already V2 format (dictionary), get keys
                basePluginNames = new HashSet<string>(((JObject)baseToken).Properties().Select(p => p.Name));
            }

            if (mineToken != null && mineToken.Type == JTokenType.Array)
            {
                minePluginNames = new HashSet<string>(mineToken.ToObject<List<string>>() ?? new List<string>());
            }
            else if (mineToken != null && mineToken.Type == JTokenType.Object)
            {
                // If mine is already V2 format (dictionary), get keys
                minePluginNames = new HashSet<string>(((JObject)mineToken).Properties().Select(p => p.Name));
            }

            // Find plugins that were intentionally removed in mine
            var removedPlugins = basePluginNames.Except(minePluginNames);
            
            // Remove those plugins from the final result, honoring the user's intent
            foreach (var pluginToRemove in removedPlugins)
            {
                finalPlugins.Remove(pluginToRemove);
            }

            // Find plugins that were added in mine (that weren't in base)
            var addedPlugins = minePluginNames.Except(basePluginNames);
            
            // Add those plugins to the final result
            foreach (var pluginToAdd in addedPlugins)
            {
                if (!finalPlugins.ContainsKey(pluginToAdd))
                {
                    finalPlugins[pluginToAdd] = JObject.FromObject(new PkgConfV2_0_Plugin { Enabled = true });
                }
            }

            return finalPlugins;
        }
        catch (Exception ex)
        {
            // Return theirs as fallback if semantic handling fails
            return theirsToken?.DeepClone() ?? new JObject();
        }
    }
}