using MigrationSystem.Core.Public;
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
    // Test DTO for the SaveLatestAsync test
    [SchemaVersion("1.0", "TestDoc")]
    public class TestDocument
    {
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
    }

    // Test DTO without SchemaVersion attribute to test fallback behavior
    public class LegacyTestDocument
    {
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
    }

    private ApplicationApi CreateApplicationApi()
    {
        var registry = new MigrationRegistry();
        // Register the test assembly to pick up our test DTO
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());
        
        var schemaGenerator = new DtoSchemaGenerator();
        var schemaRegistry = new SchemaRegistry(schemaGenerator);
        var snapshotManager = new SnapshotManager();
        var quarantineManager = new QuarantineManager(""); // Empty string instead of null for tests

        return new ApplicationApi(registry, schemaRegistry, snapshotManager, quarantineManager);
    }

    [Fact]
    public async Task LoadLatestAsync_FileWithoutMeta_InfersVersionAndLoads()
    {
        // Arrange
        var api = CreateApplicationApi();
        var tempFile = Path.GetTempFileName();
        
        // Create a legacy file without _meta block that matches TestDocument structure
        var legacyDocument = new
        {
            Name = "TestUser",
            Content = "Legacy content"
        };
        
        var json = JsonConvert.SerializeObject(legacyDocument, Formatting.Indented);
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            // Act - This should now work by inferring version 1.0 and DocType "TestDoc"
            var result = await api.LoadLatestAsync<TestDocument>(tempFile, LoadBehavior.InMemoryOnly, false);
            
            // Assert - The file should load successfully with inferred metadata
            Assert.NotNull(result);
            Assert.NotNull(result.Document);
            Assert.Equal("TestUser", result.Document.Name);
            Assert.Equal("Legacy content", result.Document.Content);
            
            // Should indicate no migration was needed (already at v1.0)
            Assert.False(result.WasMigrated);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadLatestAsync_FileWithoutMeta_TypeNameFallback_ThrowsExpectedException()
    {
        // Arrange
        var api = CreateApplicationApi();
        var tempFile = Path.GetTempFileName();
        
        // Create a legacy file for a type without SchemaVersion attribute
        var legacyDocument = new
        {
            Name = "TestUser",
            Content = "Legacy content"
        };
        
        var json = JsonConvert.SerializeObject(legacyDocument, Formatting.Indented);
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            // Act & Assert - This should fail because LegacyTestDocument doesn't have SchemaVersion
            // and the fallback to type name "LegacyTestDocument" won't find a registered DocType
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => api.LoadLatestAsync<LegacyTestDocument>(tempFile, LoadBehavior.InMemoryOnly, false));
            
            Assert.Contains("LegacyTestDocument", exception.Message);
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
        
        var document = new TestDocument 
        { 
            Name = "Test",
            Content = "test content" 
        };

        try
        {
            // Act
            await api.SaveLatestAsync(tempFile, document);

            // Assert
            Assert.True(File.Exists(tempFile));
            
            var savedContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("test content", savedContent);
            Assert.Contains("_meta", savedContent); // Should have metadata
            
            // Verify the metadata is correct
            var savedJObject = JObject.Parse(savedContent);
            var meta = savedJObject["_meta"]?.ToObject<MetaBlock>();
            Assert.NotNull(meta);
            Assert.Equal("TestDoc", meta!.DocType);
            Assert.Equal("1.0", meta.SchemaVersion);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}