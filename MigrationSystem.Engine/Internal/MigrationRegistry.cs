using MigrationSystem.Core.Contracts;
using MigrationSystem.Core.Public;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Scans assemblies to discover, register, and build paths for all available IJsonMigration implementations.
/// Enhanced with attribute-based DTO discovery using [SchemaVersion] attributes.
/// </summary>
internal class MigrationRegistry
{
    private readonly Dictionary<(Type from, Type to), object> _migrations = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, Type>> _docTypeVersionMap = new();
    private readonly Dictionary<Type, (string docType, string version)> _reverseTypeMap = new();

    public void RegisterMigrationsFromAssembly(Assembly assembly)
    {
        // --- New: Discover and register DTOs via attributes ---
        DiscoverAndRegisterVersionedDtos(assembly);

        // --- Existing logic for registering migration classes ---
        var migrationInterface = typeof(IJsonMigration<,>);
        var allDtoTypes = new HashSet<Type>();

        var migrationTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == migrationInterface));

        foreach (var type in migrationTypes)
        {
            var interfaceType = type.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == migrationInterface);
            var genericArgs = interfaceType.GetGenericArguments();
            var fromType = genericArgs[0];
            var toType = genericArgs[1];

            var instance = Activator.CreateInstance(type); // Assumes parameterless constructor
            _migrations[(fromType, toType)] = instance;
            
            allDtoTypes.Add(fromType);
            allDtoTypes.Add(toType);
        }
        
        // Verify all DTO types used in migrations have SchemaVersion attributes
        foreach (var dtoType in allDtoTypes)
        {
            if (!_reverseTypeMap.ContainsKey(dtoType))
            {
                throw new InvalidOperationException($"DTO type '{dtoType.FullName}' is used in migrations but does not have a [SchemaVersion] attribute.");
            }
        }
    }

    // --- New Private Helper Method ---
    private void DiscoverAndRegisterVersionedDtos(Assembly assembly)
    {
        var versionedTypes = assembly.GetTypes()
            .Where(t => t.IsClass && t.GetCustomAttribute<SchemaVersionAttribute>() != null)
            .ToList();

        var groupedByDocType = versionedTypes.GroupBy(t => t.GetCustomAttribute<SchemaVersionAttribute>()!.DocType);

        foreach (var group in groupedByDocType)
        {
            var docType = group.Key;
            var versionMap = group.ToDictionary(
                t => t.GetCustomAttribute<SchemaVersionAttribute>()!.Version,
                t => t
            );

            if (_docTypeVersionMap.ContainsKey(docType))
            {
                // Handle potential conflicts if registering multiple assemblies
                throw new InvalidOperationException($"Document type '{docType}' is already registered.");
            }
            
            _docTypeVersionMap[docType] = versionMap;
            
            // Populate reverse map for quick lookups
            foreach (var kvp in versionMap)
            {
                _reverseTypeMap[kvp.Value] = (docType, kvp.Key);
            }
        }
    }

    // --- Updated Method to use the new map ---
    public Type GetTypeForVersion(string docType, string version)
    {
        if (!_docTypeVersionMap.TryGetValue(docType, out var versionMap))
        {
            throw new ArgumentException($"DocType '{docType}' has no registered schemas.");
        }

        if (!versionMap.TryGetValue(version, out var type))
        {
            throw new ArgumentException($"Version '{version}' for DocType '{docType}' is not a registered schema.");
        }

        return type;
    }

    // --- New Method for getting version from type ---
    public string GetVersionFromType(Type dtoType)
    {
        var attr = dtoType.GetCustomAttribute<SchemaVersionAttribute>();
        if (attr == null)
        {
            throw new ArgumentException($"Type '{dtoType.FullName}' is not a registered versioned DTO. Does it have a [SchemaVersion] attribute?");
        }
        return attr.Version;
    }

    /// <summary>
    /// Finds an ordered sequence of migration steps to get from a source DTO type to a target DTO type.
    /// </summary>
    /// <returns>An ordered list of migration instances.</returns>
    public List<object> FindPath(Type fromType, Type toType)
    {
        if (fromType == toType) return new List<object>();

        // This uses a simple breadth-first search to find the shortest migration path.
        var queue = new Queue<List<Type>>();
        queue.Enqueue(new List<Type> { fromType });

        var visited = new HashSet<Type> { fromType };

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var currentType = path.Last();

            var nextSteps = _migrations.Keys.Where(k => k.from == currentType);

            foreach (var (from, to) in nextSteps)
            {
                if (to == toType)
                {
                    // Path found, convert types to migration instances
                    var finalPath = new List<object>();
                    for (int i = 0; i < path.Count; i++)
                    {
                        var stepFrom = path[i];
                        var stepTo = (i + 1 < path.Count) ? path[i + 1] : toType;
                        finalPath.Add(_migrations[(stepFrom, stepTo)]);
                    }
                    return finalPath;
                }

                if (!visited.Contains(to))
                {
                    visited.Add(to);
                    var newPath = new List<Type>(path) { to };
                    queue.Enqueue(newPath);
                }
            }
        }

        throw new InvalidOperationException($"No migration path could be found from {fromType.Name} to {toType.Name}.");
    }

    public IJsonMigration<TFrom, TTo> GetMigration<TFrom, TTo>() where TFrom : class where TTo : class
    {
        if (_migrations.TryGetValue((typeof(TFrom), typeof(TTo)), out var migration))
        {
            return (IJsonMigration<TFrom, TTo>)migration;
        }
        throw new InvalidOperationException($"No migration path found from {typeof(TFrom).Name} to {typeof(TTo).Name}.");
    }
}