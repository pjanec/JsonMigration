using MigrationSystem.Engine.Internal;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace MigrationSystem.Tests.Engine;

public class SnapshotManagerTests
{
    private readonly SnapshotManager _snapshotManager = new();

    [Fact]
    public async Task CreateSnapshotAsync_CreatesFileWithCorrectNameAndContent()
    {
        // Arrange
        var tempDirectory = Path.GetTempPath();
        var sourceFilePath = Path.Combine(tempDirectory, "test.json");
        var fileContent = """{"test": "data"}""";
        var version = "1.0";
        
        // Create a temporary source file
        await File.WriteAllTextAsync(sourceFilePath, fileContent);

        try
        {
            // Act
            await _snapshotManager.CreateSnapshotAsync(sourceFilePath, fileContent, version);

            // Assert
            var snapshotFiles = Directory.GetFiles(tempDirectory, "test.json.v1.0.*.snapshot.json");
            Assert.Single(snapshotFiles);
            
            var snapshotContent = await File.ReadAllTextAsync(snapshotFiles[0]);
            Assert.Equal(fileContent, snapshotContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(sourceFilePath))
                File.Delete(sourceFilePath);
            
            var snapshotFiles = Directory.GetFiles(tempDirectory, "test.json.v1.0.*.snapshot.json");
            foreach (var file in snapshotFiles)
                File.Delete(file);
        }
    }

    [Fact]
    public async Task ReadAndVerifySnapshotAsync_ValidSnapshot_ReturnsContent()
    {
        // Arrange
        var tempDirectory = Path.GetTempPath();
        var sourceFilePath = Path.Combine(tempDirectory, "test.json");
        var fileContent = """{"test": "data"}""";
        var version = "1.0";

        try
        {
            // Create a snapshot first
            await _snapshotManager.CreateSnapshotAsync(sourceFilePath, fileContent, version);
            
            var snapshotFiles = Directory.GetFiles(tempDirectory, "test.json.v1.0.*.snapshot.json");
            var snapshotPath = snapshotFiles[0];

            // Act
            var result = await _snapshotManager.ReadAndVerifySnapshotAsync(snapshotPath);

            // Assert
            Assert.Equal(fileContent, result);
        }
        finally
        {
            // Cleanup
            var snapshotFiles = Directory.GetFiles(tempDirectory, "test.json.v1.0.*.snapshot.json");
            foreach (var file in snapshotFiles)
                File.Delete(file);
        }
    }

    [Fact]
    public async Task ReadAndVerifySnapshotAsync_CorruptedSnapshot_ThrowsSnapshotIntegrityException()
    {
        // Arrange
        var tempDirectory = Path.GetTempPath();
        var sourceFilePath = Path.Combine(tempDirectory, "test.json");
        var fileContent = """{"test": "data"}""";
        var version = "1.0";

        try
        {
            // Create a snapshot first
            await _snapshotManager.CreateSnapshotAsync(sourceFilePath, fileContent, version);
            
            var snapshotFiles = Directory.GetFiles(tempDirectory, "test.json.v1.0.*.snapshot.json");
            var snapshotPath = snapshotFiles[0];

            // Corrupt the snapshot by modifying its content
            var corruptedContent = """{"test": "corrupted_data"}""";
            await File.WriteAllTextAsync(snapshotPath, corruptedContent);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<SnapshotIntegrityException>(
                () => _snapshotManager.ReadAndVerifySnapshotAsync(snapshotPath));
            
            Assert.Contains("Snapshot integrity check failed", exception.Message);
        }
        finally
        {
            // Cleanup
            var snapshotFiles = Directory.GetFiles(tempDirectory, "test.json.v1.0.*.snapshot.json");
            foreach (var file in snapshotFiles)
                File.Delete(file);
        }
    }
}