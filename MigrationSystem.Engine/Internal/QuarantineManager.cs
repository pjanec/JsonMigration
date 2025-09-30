using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

/// <summary>
/// Handles all file system operations related to quarantining failed documents.
/// </summary>
internal class QuarantineManager
{
    private readonly string _quarantineDirectory;

    public QuarantineManager(string quarantineDirectory)
    {
        _quarantineDirectory = quarantineDirectory;
        if (!string.IsNullOrEmpty(_quarantineDirectory))
        {
            Directory.CreateDirectory(_quarantineDirectory);
        }
    }

    /// <summary>
    /// Moves a failed file to the quarantine directory and writes a detailed report.
    /// </summary>
    /// <param name="sourceFilePath">The path to the problematic file.</param>
    /// <param name="record">The diagnostic record explaining the failure.</param>
    /// <returns>The path to the generated quarantine report file.</returns>
    public async Task<string?> QuarantineFileAsync(string sourceFilePath, QuarantineRecord record)
    {
        if (string.IsNullOrEmpty(_quarantineDirectory))
        {
            // If no directory is configured, we cannot quarantine.
            // Log a warning and return.
            return null;
        }

        var sourceFileName = Path.GetFileName(sourceFilePath);
        // Use short hash for brevity, handle cases where hash might be shorter than 8 chars
        var contentHash = record.ContentHash.Length >= 8 ? record.ContentHash.Substring(0, 8) : record.ContentHash;
        
        // Create unique, traceable names for the quarantined files
        var quarantinedDataFileName = $"{Path.GetFileNameWithoutExtension(sourceFileName)}_{contentHash}{Path.GetExtension(sourceFileName)}";
        var quarantineReportFileName = $"{quarantinedDataFileName}.quarantine.json";

        var quarantinedDataPath = Path.Combine(_quarantineDirectory, quarantinedDataFileName);
        var quarantineReportPath = Path.Combine(_quarantineDirectory, quarantineReportFileName);

        // 1. Move the problematic file to the quarantine directory
        File.Move(sourceFilePath, quarantinedDataPath, overwrite: true);

        // 2. Write the detailed diagnostic report
        var reportJson = JsonConvert.SerializeObject(record, Formatting.Indented);
        await File.WriteAllTextAsync(quarantineReportPath, reportJson, Encoding.UTF8);

        return quarantineReportPath;
    }
}