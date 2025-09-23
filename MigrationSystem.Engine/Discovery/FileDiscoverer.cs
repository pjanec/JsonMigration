using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Core.Contracts;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Discovery;

// This internal class implements the public discovery facade.
internal class FileDiscoverer : IDiscoveryService
{
    // A real implementation would discover these via reflection/DI.
    private readonly Dictionary<string, IDiscoveryRule> _rules = new();

    // A real implementation would have more complex logic to parse the manifest
    // and execute the IDiscoveryRule instances. This is a simplified placeholder.
    public async Task<MigrationManifest> LoadManifestAsync(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            // Return empty manifest if file doesn't exist
            return new MigrationManifest(new List<string>(), new List<DiscoveryRuleDefinition>());
        }
        
        var json = await File.ReadAllTextAsync(manifestPath);
        return JsonConvert.DeserializeObject<MigrationManifest>(json) ?? 
               new MigrationManifest(new List<string>(), new List<DiscoveryRuleDefinition>());
    }

    public Task<IEnumerable<string>> DiscoverManagedFilesAsync(MigrationManifest manifest)
    {
        var files = new HashSet<string>();

        if (manifest.IncludePaths != null)
        {
            foreach (var path in manifest.IncludePaths) files.Add(path);
        }

        if (manifest.DiscoveryRules != null)
        {
            foreach (var ruleDef in manifest.DiscoveryRules)
            {
                if (_rules.TryGetValue(ruleDef.RuleName, out var rule))
                {
                    foreach (var file in rule.Execute(ruleDef.Parameters)) files.Add(file);
                }
            }
        }
        
        return Task.FromResult<IEnumerable<string>>(files);
    }
}