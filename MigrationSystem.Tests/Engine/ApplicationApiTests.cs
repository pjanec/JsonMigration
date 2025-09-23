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

    private ApplicationApi CreateApplicationApi()
    {
        var registry = new MigrationRegistry();
        // Register the test assembly to pick up our test DTO
        registry.RegisterMigrationsFromAssembly(Assembly.GetExecutingAssembly());
        
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
            // Act & Assert - Files without _meta blocks should throw an exception
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => api.LoadLatestAsync<TestDocument>(tempFile, LoadBehavior.InMemoryOnly, false));
            
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