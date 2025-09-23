# **Transparent Versioning & Migration System - Internal Design**

## **1. Architectural Overview**

The library is designed as a set of layered, single-responsibility modules. The core principle is the separation of orchestration, logic, and data contracts. This document details the internal components that power the public API facades.

Dependencies flow from the high-level public facades, through the orchestrating engine components, down to the specialized logic and core contracts. This layered approach ensures high cohesion, low coupling, and excellent testability.

graph TD
    subgraph Public API Facades
        A[ApplicationApi]
        B[OperationalApi]
        C[DataApi]
    end
    subgraph Internal Engine Components
        D[MigrationRunner] --> E[MigrationPlanner]
        D --> F[ThreeWayMerger]
        D --> G[SnapshotManager]
        E --> H[FileDiscoverer]
        E --> I[MigrationRegistry]
        F --> I
    end
    subgraph Core & Logic
        J[Migration Implementations]
        K[Discovery Rule Implementations]
        L[Core DTOs & Contracts]
    end
    A --> D
    B --> E
    B --> D
    C --> D
    H --> K
    I --> J
    J --> L
    K --> L

## **2. Core Module: The Foundation**

This module contains no logic. It is the foundational set of shared contracts, interfaces, and data structures used by all other parts of the system.

* **Responsibilities**: Define the stable, public contracts of the library.
* **Key Components**:
  * **IJsonMigration<TFrom, TTo>**: The asynchronous, DI-friendly interface that all migration classes must implement.
  * **ISemanticMigration**: The optional interface for providing custom three-way merge logic for specific properties.
  * **Versioned DTOs**: The set of C# records that define the schema for each document type at each version (e.g., PkgConfV1, PkgConfV2). These are decorated with custom validation attributes.
  * **Public Data Contracts**: The immutable records used by the public API (VersionedDocument, DocumentBundle, Snapshot, MigrationPlan, MigrationResult, QuarantineRecord).
  * **Custom Validation Attributes**: The set of attributes used to define validation rules on DTOs (e.g., [NumberRange], [StringPattern]).

---

## **3. The Engine: Internal Components**

The Engine is the heart of the library. It is a collection of internal, stateless services that are orchestrated by the public API facades to perform the work.

### **3.1. MigrationRegistry**

* **Responsibilities**: To discover and provide a queryable map of all available migration paths.
* **Process**:
  1. On initialization, it is provided with the application's assembly.
  2. It uses reflection to find all concrete classes that implement IJsonMigration<TFrom, TTo>.
  3. It builds an internal graph or dictionary that maps a (DocType, FromVersion, ToVersion) tuple to a specific migration class instance.
* **Inputs**: A .NET Assembly.
* **Outputs**: A method FindPath(from, to) which returns an ordered list of migration steps.

### **3.2. DtoSchemaGenerator & SchemaRegistry**

* **Responsibilities**: To provide on-demand, code-first JSON Schema validation.
* **Process**:
  1. The SchemaRegistry maintains an in-memory cache of generated schemas.
  2. When a schema for a given DTO type is requested for the first time, the registry calls the DtoSchemaGenerator.
  3. The DtoSchemaGenerator uses reflection (and a library like NJsonSchema) to recursively scan the DTO, its properties, and its custom validation attributes to build a formal JsonSchema object.
  4. The SchemaRegistry caches this generated schema for all subsequent requests.
* **Inputs**: A C# Type object (e.g., typeof(PkgConfV2)).
* **Outputs**: A compiled JsonSchema object.

### **3.3. FileDiscoverer**

* **Responsibilities**: To implement the file discovery workflow. This module transforms high-level discovery rules into concrete file paths.
* **Process**:
  1. It receives a Manifest object containing multiple DiscoveryRule configurations.
  2. For each rule, it determines which IDiscoveryRule implementation should be used (e.g., FileExtensionRule, FilePatternRule, PerDirectoryRule).
  3. It executes each rule's discovery logic and aggregates the results.
  4. It returns a comprehensive list of file paths that should be under management.
* **Inputs**: A Manifest object.
* **Outputs**: IEnumerable<string> of file paths.

### **3.4. SnapshotManager**

* **Responsibilities**: To manage all file I/O and integrity checks for snapshot files.
* **Process**:
  1. **Creating Snapshots**: For each source file, it generates a timestamped, hashed snapshot filename and writes the content atomically.
  2. **Reading & Verifying Snapshots**: It reads a snapshot file and verifies its integrity by matching its content hash against the hash in its filename.
  3. **Cleanup**: It provides methods to delete obsolete snapshots as part of the garbage collection process.
* **Inputs**: File paths, content strings.
* **Outputs**: Verified content strings, file paths.

### **3.5. MigrationPlanner**

* **Responsibilities**: To perform a safe, read-only analysis and produce a detailed MigrationPlan.
* **Process**:
  1. Takes a collection of DocumentBundle objects as input.
  2. For each document, it queries the MigrationRegistry to find the required migration path.
  3. It analyzes the AvailableSnapshots to determine if a STANDARD_UPGRADE or a THREE_WAY_MERGE is required.
  4. It compiles these findings into a list of PlanAction objects.
  5. It returns a final MigrationPlan containing the complete list of actions.
* **Inputs**: IEnumerable<DocumentBundle>, target version.
* **Outputs**: A MigrationPlan object.

### **3.6. ThreeWayMerger**

* **Responsibilities**: To contain the complex, state-sensitive logic for a lossless merge using a hybrid semantic and structural approach.
* **Process**:
  1. Receives the three JObjects: **BASE**, **MINE**, and **THEIRS**, along with the active migration instance.
  2. It first checks if the migration implements `ISemanticMigration`. If so, it delegates the merging of any properties handled by the interface to the migration's custom logic.
  3. For all remaining, unhandled properties, it falls back to a formal, patch-based merge. It uses a JSON Patch library to generate two patch documents: diff(MINE, BASE) and diff(THEIRS, BASE).
  4. It intelligently combines these two patches, enforcing the conflict resolution policy ("Mine Wins" by default), and applies the final merged patch to the BASE to get the result.
  5. The final document is a combination of the semantically-merged and structurally-merged properties.
* **Inputs**: Three JObjects, one migration object.
* **Outputs**: A single, merged JObject.

### **3.7. MigrationRunner**

* **Responsibilities**: To execute a MigrationPlan. This is the primary component that orchestrates writes and state changes.
* **Process**:
  1. Takes a MigrationPlan and a collection of DocumentBundle objects as input.
  2. For each PlanAction, it executes the required workflow in a try...catch block.
  3. **For an upgrade**: It calls SnapshotManager to create a snapshot, then runs the migration chain, and prepares the final DataMigrationResult.
  4. **For a merge**: It calls SnapshotManager to verify the input snapshots, delegates the core logic to the ThreeWayMerger, and prepares the DataMigrationResult, including a list of snapshots to be deleted.
  5. **On failure**: The catch block creates a detailed QuarantineRecord and adds it to a list of failed operations.
  6. After processing all actions, it compiles and returns a final MigrationResult object containing lists of all successful and failed operations.
* **Inputs**: MigrationPlan, IEnumerable<DocumentBundle>.
* **Outputs**: A MigrationResult object.

---

## **4. Public API Facades**

The public-facing interfaces are thin facades that orchestrate the internal engine components to provide a clean, intuitive experience for the consumer.

### **4.1. ApplicationApi**

* **Responsibilities**: To provide the main document loading and saving operations with validation.
* **Key Methods**:
  * `LoadLatestAsync<T>()`: Load and migrate a document to the latest schema version.
  * `SaveAsync<T>()`: Validate and save a document with proper metadata.

### **4.2. OperationalApi**

* **Responsibilities**: To provide file-system-based migration operations.
* **Key Methods**:
  * `PlanUpgradeFromManifestAsync()`: Create a migration plan from discovered files.
  * `ExecutePlanAgainstFileSystemAsync()`: Execute a plan against the file system.
  * `RetryFailedFileSystemAsync()`: Retry failed operations.
  * `GarbageCollectSnapshotsAsync()`: Clean up obsolete snapshots.

### **4.3. DataApi**

* **Responsibilities**: To provide in-memory data migration operations.
* **Key Methods**:
  * `ExecuteUpgradeAsync()`: Perform an upgrade operation on in-memory data.
  * `ExecuteDowngradeAsync()`: Perform a downgrade operation on in-memory data.
  * `MigrateToLatestAsync<T>()`: Migrate in-memory data to the latest version.

---

## **5. Data Flow & Orchestration Example: File-Based Downgrade**

This shows how the IOperationalApi acts as a thin wrapper over the internal components.

1. **IOperationalApi.PlanRollbackFromManifestAsync()** is called
2. **FileDiscoverer** loads the manifest and discovers files
3. **SnapshotManager** loads and verifies existing snapshots for each file
4. **MigrationPlanner** analyzes each file and creates a rollback plan
5. **IOperationalApi.ExecutePlanAgainstFileSystemAsync()** executes the plan
6. **MigrationRunner** processes each action in the plan
7. **SnapshotManager** creates new snapshots and cleans up old ones
8. Results are returned to the caller

This layered approach ensures that each component has a single, well-defined responsibility while providing flexibility for different usage patterns through the public API facades.