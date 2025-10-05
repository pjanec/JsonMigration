using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System;

namespace MigrationSystem.Core.Public.DataContracts;

// --- Core Data Structures ---

/// <summary>
/// Contains the essential metadata that describes a versioned document.
/// </summary>
/// <param name="DocType">A "magic string" that uniquely identifies the type of document (e.g., "PkgConf", "UserProfile").</param>
/// <param name="SchemaVersion">The MAJOR.MINOR version of the document's schema (e.g., "1.0", "2.1").</param>
public sealed record MetaBlock(string DocType, string SchemaVersion);

/// <summary>
/// Represents a verifiable snapshot of a document at a specific version.
/// </summary>
/// <param name="Data">The data-only JObject of the snapshot.</param>
/// <param name="Metadata">The metadata describing the snapshot's content.</param>
public sealed record Snapshot(JObject Data, MetaBlock Metadata);

/// <summary>
/// Represents a single, versioned document with a unique identifier.
/// </summary>
/// <param name="Identifier">A unique string to identify this document (e.g., a GUID, a database primary key, a file path).</param>
/// <param name="Data">The data-only JObject.</param>
/// <param name="Metadata">The metadata describing the data's schema.</param>
public sealed record VersionedDocument(string Identifier, JObject Data, MetaBlock Metadata);

/// <summary>
/// A self-contained package representing a document and all its known historical snapshots.
/// </summary>
/// <param name="Document">The current state of the document.</param>
/// <param name="AvailableSnapshots">All previously persisted snapshots for this document.</param>
public sealed record DocumentBundle(VersionedDocument Document, IEnumerable<Snapshot> AvailableSnapshots);

/// <summary>
/// A structured representation of the MigrationManifest.json file.
/// </summary>
/// <param name="IncludePaths">A list of explicit, absolute file paths to include in operations.</param>
/// <param name="DiscoveryRules">A list of named rules for discovering files in directory trees.</param>
public sealed record MigrationManifest(
    IReadOnlyList<string> IncludePaths,
    IReadOnlyList<DiscoveryRuleDefinition> DiscoveryRules
);

/// <summary>
/// Defines a single discovery rule within a manifest.
/// </summary>
/// <param name="RuleName">The "well-known name" of the rule to execute (must match a registered IDiscoveryRule).</param>
/// <param name="Parameters">A JObject containing the parameters to pass to the rule's Execute method.</param>
public sealed record DiscoveryRuleDefinition(string RuleName, JObject Parameters);

// --- Enhanced Schema Management Contracts ---

/// <summary>
/// Represents the top-level structure of the schema config JSON file.
/// </summary>
public sealed record SchemaConfigFile(IReadOnlyDictionary<string, string> SchemaVersions);

/// <summary>
/// Represents the schema_versions.json file that locks schema versions for all known document types.
/// </summary>
public sealed record SchemaConfig(IReadOnlyDictionary<string, string> SchemaVersions);

/// <summary>
/// Represents the on-disk transaction journal file for resilient migrations.
/// </summary>
public sealed record TransactionJournal(
    string TransactionId,
    string Status, // "InProgress", "Committed", "RolledBack"
    IReadOnlyList<JournalOperation> Operations
);

/// <summary>
/// Represents a single operation within a transaction journal.
/// </summary>
public sealed record JournalOperation(
    string FilePath,
    string Status // "Pending", "BackedUp", "Processing", "Completed"
);

// --- Planning & Execution Contracts ---

public sealed record MigrationPlan(PlanHeader Header, IReadOnlyList<PlanAction> Actions);
public sealed record PlanHeader(string TargetVersion, DateTime GeneratedAtUtc);
public sealed record PlanAction(string DocumentIdentifier, ActionType ActionType, string Details);
public enum ActionType { STANDARD_UPGRADE, THREE_WAY_MERGE, STANDARD_DOWNGRADE, SKIP, QUARANTINE }

public sealed record MigrationResult(ResultSummary Summary, IReadOnlyList<SuccessfulMigration> SuccessfulDocuments, IReadOnlyList<FailedMigration> FailedDocuments);
public sealed record ResultSummary(string Status, TimeSpan Duration, int Processed, int Succeeded, int Failed, int Skipped);
public sealed record SuccessfulMigration(string Identifier, DataMigrationResult Result);
public sealed record FailedMigration(string Identifier, JObject OriginalData, MetaBlock OriginalMetadata, QuarantineRecord QuarantineRecord);

public sealed record DataMigrationResult
{
    public required JObject Data { get; init; }
    public required MetaBlock NewMetadata { get; init; }
    public IReadOnlyList<Snapshot> SnapshotsToPersist { get; init; } = [];
    public IReadOnlyList<MetaBlock> SnapshotsToDelete { get; init; } = [];
}
public sealed record GcResult; // To be detailed further.

// --- Error Handling & Application API Contracts ---

public sealed record QuarantineRecord(string DocumentIdentifier, string Reason, string Details, string ContentHash, string SuggestedNextSteps);
public sealed record LoadResult<T>(T Document, bool WasMigrated);

public enum LoadBehavior
{
    InMemoryOnly,
    MigrateOnDisk
}