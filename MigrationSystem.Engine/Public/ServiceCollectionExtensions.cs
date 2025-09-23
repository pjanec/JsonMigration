using Microsoft.Extensions.DependencyInjection;
using MigrationSystem.Core.Public;
using MigrationSystem.Engine.Internal;
using System;

namespace MigrationSystem.Engine.Public;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Migration System services into the DI container.
    /// </summary>
    public static IServiceCollection AddMigrationSystem(
        this IServiceCollection services,
        Action<MigrationSystemBuilder> configure)
    {
        var builder = new MigrationSystemBuilder();
        configure(builder);

        // Register internal components as singletons as they are stateless
        services.AddSingleton<MigrationRegistry>();
        services.AddSingleton<DtoSchemaGenerator>();
        services.AddSingleton<SchemaRegistry>();
        services.AddSingleton<SnapshotManager>();
        services.AddSingleton<MigrationPlanner>();
        services.AddSingleton<ThreeWayMerger>();
        services.AddSingleton<MigrationRunner>();

        // Register the main facade, which will be built by the builder.
        // The builder itself will resolve the dependencies from the container.
        services.AddSingleton<IMigrationSystem>(sp => builder.Build(sp));

        return services;
    }
}