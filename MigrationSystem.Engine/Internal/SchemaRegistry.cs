using NJsonSchema;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Provides on-demand, cached JSON Schemas derived from DTO types.
/// </summary>
internal class SchemaRegistry
{
    private readonly DtoSchemaGenerator _generator;
    private readonly ConcurrentDictionary<Type, JsonSchema> _schemaCache = new();

    public SchemaRegistry(DtoSchemaGenerator generator)
    {
        _generator = generator;
    }

    /// <summary>
    /// Gets a compiled schema for the given DTO type, generating it if not already cached.
    /// </summary>
    public JsonSchema GetSchema(Type dtoType)
    {
        return _schemaCache.GetOrAdd(dtoType, (type) => _generator.GenerateSchemaForType(type));
    }
}