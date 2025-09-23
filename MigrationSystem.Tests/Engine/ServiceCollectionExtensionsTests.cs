using Microsoft.Extensions.DependencyInjection;
using MigrationSystem.Core.Public;
using MigrationSystem.Engine.Public;
using System.Reflection;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMigrationSystem_RegistersServicesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMigrationSystem(builder =>
        {
            builder.WithMigrationsFromAssembly(Assembly.GetExecutingAssembly())
                   .WithDefaultManifestPath("./manifest.json")
                   .WithQuarantineDirectory("./quarantine");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var migrationSystem = serviceProvider.GetService<IMigrationSystem>();
        Assert.NotNull(migrationSystem);
        
        // Verify that internal services are registered and can be resolved
        var migrationRegistry = serviceProvider.GetService<MigrationSystem.Engine.Internal.MigrationRegistry>();
        Assert.NotNull(migrationRegistry);
        
        var snapshotManager = serviceProvider.GetService<MigrationSystem.Engine.Internal.SnapshotManager>();
        Assert.NotNull(snapshotManager);
        
        var schemaRegistry = serviceProvider.GetService<MigrationSystem.Engine.Internal.SchemaRegistry>();
        Assert.NotNull(schemaRegistry);
    }
}