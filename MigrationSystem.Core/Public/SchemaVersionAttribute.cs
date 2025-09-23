using System;

namespace MigrationSystem.Core.Public;

/// <summary>
/// Decorates a DTO class to associate it with a specific document type and schema version.
/// This is the primary mechanism for the MigrationRegistry to discover and map versioned types.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SchemaVersionAttribute : Attribute
{
    /// <summary>
    /// The semantic version of the schema that this DTO represents (e.g., "1.0", "2.1.3").
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// The type of document this schema applies to (e.g., "PkgConf", "UserProfile").
    /// </summary>
    public string DocType { get; }

    public SchemaVersionAttribute(string version, string docType)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentNullException(nameof(version));
        
        if (string.IsNullOrWhiteSpace(docType))
            throw new ArgumentNullException(nameof(docType));

        Version = version;
        DocType = docType;
    }
}