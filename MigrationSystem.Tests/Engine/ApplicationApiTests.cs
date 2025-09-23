using MigrationSystem.Core.Public.DataContracts;
using MigrationSystem.Engine.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class ApplicationApiTests
{
    private ApplicationApi CreateApplicationApi()
    {
        var registry = new MigrationRegistry();
        // Don't register migrations to avoid type conflicts
        
        var schemaGenerator = new DtoSchemaGenerator();
        var schemaRegistry = new SchemaRegistry(schemaGenerator);
        var snapshotManager = new SnapshotManager();
        var quarantineManager = new QuarantineManager(null); // No quarantine directory for tests

        return new ApplicationApi(registry, schemaRegistry, snapshotManager, quarantineManager);
    }

    [Fact]
    public async Task LoadLatestAsync_FileWithoutMeta_ThrowsException()
    {
        // Arrange
        var api = CreateApplicationApi();
        var tempFile = Path.GetTempFileName();
        
        var testDocument = new
        {
            Name = "TestUser"
            // No _meta block
        };
        
        var json = JsonConvert.SerializeObject(testDocument, Formatting.Indented);
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => api.LoadLatestAsync<string>(tempFile, LoadBehavior.InMemoryOnly, false));
            
            Assert.Contains("does not contain a _meta block", exception.Message);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SaveLatestAsync_CreatesFile()
    {
        // Arrange
        var api = CreateApplicationApi();
        var tempFile = Path.GetTempFileName();
        
        var document = "test content";

        try
        {
            // Act
            await api.SaveLatestAsync(tempFile, document);

            // Assert
            Assert.True(File.Exists(tempFile));
            
            var savedContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("test content", savedContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}