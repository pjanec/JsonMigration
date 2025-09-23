using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MigrationSystem.Core.Contracts;

/// <summary>
/// Defines a contract for a named, parameter-driven file discovery rule,
/// referenced in the MigrationManifest.json.
/// </summary>
public interface IDiscoveryRule
{
    /// <summary>
    /// The "well-known name" of the rule.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the discovery logic. The implementation is responsible for
    /// performing a "safe peek" to verify the doc_type of found files.
    /// </summary>
    /// <param name="parameters">A JObject containing the parameters for this rule
    /// from the manifest.</param>
    /// <returns>An enumerable of verified, absolute file paths.</returns>
    IEnumerable<string> Execute(JObject parameters);
}