using Newtonsoft.Json.Linq;

namespace MigrationSystem.Core.Public;

/// <summary>
/// Defines an optional interface for a migration class to provide custom,
/// domain-specific logic for a three-way merge operation on specific properties.
/// </summary>
public interface ISemanticMigration
{
    /// <summary>
    /// Determines whether this handler can provide custom merge logic for a given property.
    /// </summary>
    bool CanHandleProperty(string propertyName);

    /// <summary>
    /// Performs a three-way merge for a specific property.
    /// </summary>
    JToken MergeProperty(string propertyName, JToken? baseToken, JToken? mineToken, JToken? theirsToken);
}