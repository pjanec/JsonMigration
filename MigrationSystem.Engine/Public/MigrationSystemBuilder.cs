using MigrationSystem.Core.Public;
using MigrationSystem.Engine.Internal;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;

namespace MigrationSystem.Engine.Public;

public class MigrationSystemBuilder
{
    private Assembly _migrationsAssembly;
    private string _manifestPath;
    private string _quarantineDir;

    // Public configuration methods remain the same...
    public MigrationSystemBuilder WithMigrationsFromAssembly(Assembly assembly)
    {
        _migrationsAssembly = assembly;
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

    // The Build method is now internal and accepts an IServiceProvider
    internal IMigrationSystem Build(IServiceProvider serviceProvider)
    {
        // Resolve the singleton registry that was registered by the extension method
        var registry = serviceProvider.GetRequiredService<MigrationRegistry>();
        registry.RegisterMigrationsFromAssembly(_migrationsAssembly);
        
        // Resolve other services
        var snapshotManager = serviceProvider.GetRequiredService<SnapshotManager>();
        var schemaRegistry = serviceProvider.GetRequiredService<SchemaRegistry>();
        
        // Compose the final facade implementation with its dependencies
        return new MigrationSystemFacade(registry, snapshotManager, schemaRegistry /*, ... other services */);
    }
}
