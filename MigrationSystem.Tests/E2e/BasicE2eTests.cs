using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Engine.Public;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.E2e;

public class BasicE2eTests
{
    [Fact]
    public async Task Debug_MigrationSystemRegistration()
    {
        // Build the migration system
        var services = new ServiceCollection();
        services.AddMigrationSystem(options =>
        {
            options.WithMigrationsFromAssembly(typeof(BasicE2eTests).Assembly);
        });
        var serviceProvider = services.BuildServiceProvider();
        var migrationSystem = serviceProvider.GetRequiredService<IMigrationSystem>();

        // Test that we can create a simple plan
        var emptyBundles = new DocumentBundle[0];
        var plan = await migrationSystem.OperationalData.PlanUpgradeAsync(emptyBundles);
        
        plan.Should().NotBeNull();
        plan.Header.TargetVersion.Should().Be("2.0");
    }

    [Fact]
    public async Task Debug_ManifestLoading()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // Create a simple manifest
            var manifestPath = Path.Combine(tempDir, "manifest.json");
            var testFile = Path.Combine(tempDir, "test.json");
            
            var manifestContent = $@"{{ ""includePaths"": [ ""{testFile.Replace("\\", "\\\\")}"" ], ""discoveryRules"": [] }}";
            await File.WriteAllTextAsync(manifestPath, manifestContent);
            
            // Create a test file
            var fileContent = @"{
  ""_meta"": {
    ""DocType"": ""PkgConf"",
    ""SchemaVersion"": ""1.0""
  },
  ""timeout"": 30,
  ""plugins"": [""auth""]
}";
            await File.WriteAllTextAsync(testFile, fileContent);

            var services = new ServiceCollection();
            services.AddMigrationSystem(options =>
            {
                options.WithMigrationsFromAssembly(typeof(BasicE2eTests).Assembly);
            });
            var serviceProvider = services.BuildServiceProvider();
            var migrationSystem = serviceProvider.GetRequiredService<IMigrationSystem>();

            // Test manifest loading
            var manifest = await migrationSystem.Discovery.LoadManifestAsync(manifestPath);
            manifest.Should().NotBeNull();
            
            // Test file discovery
            var files = await migrationSystem.Discovery.DiscoverManagedFilesAsync(manifest);
            files.Should().Contain(testFile);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}