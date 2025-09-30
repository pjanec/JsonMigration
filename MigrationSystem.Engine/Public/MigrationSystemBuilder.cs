using MigrationSystem.Core.Public;
using MigrationSystem.Engine.Internal;
using MigrationSystem.Engine.Discovery;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MigrationSystem.Engine.Public;

public class MigrationSystemBuilder
{
    // Change this to a list of assemblies
    private readonly List<Assembly> _migrationsAssemblies = new();
    private string? _manifestPath;
    private string? _quarantineDir;

    public MigrationSystemBuilder WithMigrationsFromAssembly(Assembly assembly)
    {
        // Add the assembly to the list if it's not already there
        if (!_migrationsAssemblies.Contains(assembly))
        {
            _migrationsAssemblies.Add(assembly);
        }
        return this;
    }

    public MigrationSystemBuilder WithDefaultManifestPath(string path)
    {
        _manifestPath = path;
        return this;
    }

    public MigrationSystemBuilder WithQuarantineDirectory(string path)
    {
        _quarantineDir = path;
        return this;
    }

    internal IMigrationSystem Build(IServiceProvider serviceProvider)
    {
        var registry = serviceProvider.GetRequiredService<MigrationRegistry>();
        
        // Loop through all registered assemblies and register their migrations
        foreach (var assembly in _migrationsAssemblies)
        {
            registry.RegisterMigrationsFromAssembly(assembly);
        }
        
        var snapshotManager = serviceProvider.GetRequiredService<SnapshotManager>();
        var schemaRegistry = serviceProvider.GetRequiredService<SchemaRegistry>();
        
        // Create the QuarantineManager with the configured path, providing empty string as fallback
        var quarantineManager = new QuarantineManager(_quarantineDir ?? "");
        
        // Create the FileDiscoverer with the configured default manifest path, providing empty string as fallback
        var fileDiscoverer = new FileDiscoverer(_manifestPath ?? "");
        
        // Compose the final facade implementation with its dependencies
        return new MigrationSystemFacade(registry, snapshotManager, schemaRegistry, quarantineManager, fileDiscoverer);
    }
}
