using Microsoft.Extensions.DependencyInjection;
using MigrationSystem.Core.Public;
using MigrationSystem.Engine.Public;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class SchemaConfigTests
{
    [Fact]
    public async Task GetLatestSchemaVersionsAsync_ReturnsRegisteredTypes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMigrationSystem(options =>
        {
            options.WithMigrationsFromAssembly(typeof(SchemaConfigTests).Assembly);
        });
        var serviceProvider = services.BuildServiceProvider();
        var migrationSystem = serviceProvider.GetRequiredService<IMigrationSystem>();

        // Act
        var versions = await migrationSystem.Operations.GetLatestSchemaVersionsAsync();

        // Assert
        Assert.NotEmpty(versions);
        Assert.True(versions.ContainsKey("PkgConf"));
        Assert.Equal("2.0", versions["PkgConf"]);
        
        Assert.True(versions.ContainsKey("User"));
        Assert.Equal("2.0", versions["User"]);
        
        Assert.True(versions.ContainsKey("TestDoc"));
        Assert.Equal("1.0", versions["TestDoc"]);
    }

    [Fact]
    public async Task WriteSchemaConfigAsync_CreatesValidFile()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMigrationSystem(options =>
        {
            options.WithMigrationsFromAssembly(typeof(SchemaConfigTests).Assembly);
        });
        var serviceProvider = services.BuildServiceProvider();
        var migrationSystem = serviceProvider.GetRequiredService<IMigrationSystem>();

        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            await migrationSystem.Operations.WriteSchemaConfigAsync(tempFile);

            // Assert
            Assert.True(File.Exists(tempFile));
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("PkgConf", content);
            Assert.Contains("2.0", content);
            Assert.Contains("SchemaVersions", content);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}