using MigrationSystem.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Scans assemblies to discover, register, and build paths for all available IJsonMigration implementations.
/// </summary>
internal class MigrationRegistry
{
    private readonly Dictionary<(Type from, Type to), object> _migrations = new();
    private readonly Dictionary<(string docType, string version), Type> _typeMap = new();
    private readonly Dictionary<Type, (string docType, string version)> _reverseTypeMap = new();

    public void RegisterMigrationsFromAssembly(Assembly assembly)
    {
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
        
        foreach (var dtoType in allDtoTypes)
        {
            // A more robust inference might use a custom attribute on the DTO
            var nameParts = dtoType.Name.Split('V');
            if (nameParts.Length == 2)
            {
                var docType = nameParts[0];
                var version = nameParts[1].Replace('_', '.');
                _typeMap[(docType, version)] = dtoType;
                _reverseTypeMap[dtoType] = (docType, version);
            }
        }
    }

    public Type GetTypeForVersion(string docType, string version)
    {
        if (_typeMap.TryGetValue((docType, version), out var type))
        {
            return type;
        }
        throw new InvalidOperationException($"No DTO type found for DocType '{docType}' and Version '{version}'.");
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