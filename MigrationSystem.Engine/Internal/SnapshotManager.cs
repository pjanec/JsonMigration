using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Manages all file I/O and integrity checks for snapshot files.
/// </summary>
internal class SnapshotManager
{
    /// <summary>
    /// Creates a verifiable snapshot file on disk.
    /// </summary>
    /// <param name="sourceFilePath">The path of the original file being snapshotted.</param>
    /// <param name="fileContent">The raw string content of the file to be saved in the snapshot.</param>
    /// <param name="version">The schema version of the content.</param>
    public async Task CreateSnapshotAsync(string sourceFilePath, string fileContent, string version)
    {
        var hash = ComputeShortSha256(fileContent);
        var snapshotFileName = $"{Path.GetFileName(sourceFilePath)}.v{version}.{hash}.snapshot.json";
        var snapshotDirectory = Path.GetDirectoryName(sourceFilePath);
        var snapshotPath = Path.Combine(snapshotDirectory, snapshotFileName);

        await WriteFileAtomicallyAsync(snapshotPath, fileContent);
    }

    /// <summary>
    /// Reads the content of a snapshot file, verifying its integrity by matching its content hash against the hash in its filename.
    /// </summary>
    /// <param name="snapshotPath">The full path to the snapshot file.</param>
    /// <returns>The verified content of the snapshot.</returns>
    /// <exception cref="SnapshotIntegrityException">Thrown if the file is corrupt or has been tampered with.</exception>
    public async Task<string> ReadAndVerifySnapshotAsync(string snapshotPath)
    {
        var fileName = Path.GetFileName(snapshotPath);
        var parts = fileName.Split('.');
        if (parts.Length < 5) // e.g., {name}.json.v{version}.{hash}.snapshot.json
        {
            throw new SnapshotIntegrityException($"Invalid snapshot filename format: {fileName}");
        }

        var expectedHash = parts[^3];
        var content = await File.ReadAllTextAsync(snapshotPath, Encoding.UTF8);
        var actualHash = ComputeShortSha256(content);

        if (actualHash != expectedHash)
        {
            throw new SnapshotIntegrityException($"Snapshot integrity check failed for '{fileName}'. Expected hash '{expectedHash}' but got '{actualHash}'. The file may be corrupt.");
        }

        return content;
    }
    
    // This helper method implements the atomic write pattern.
    private async Task WriteFileAtomicallyAsync(string filePath, string content)
    {
        var tempFilePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFilePath, content, Encoding.UTF8);
        File.Move(tempFilePath, filePath, overwrite: true);
    }
    
    private string ComputeShortSha256(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant(); // Use first 4 bytes for an 8-char hash
    }
}

/// <summary>
/// Exception thrown when a snapshot file's integrity check fails.
/// </summary>
internal class SnapshotIntegrityException : Exception
{
    public SnapshotIntegrityException(string message) : base(message) { }
}