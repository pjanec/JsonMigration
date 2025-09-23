# **Transparent Versioning & Migration System \- Internal Design**

## **1\. Architectural Overview**

The library is designed as a set of layered, single-responsibility modules. The core principle is the separation of orchestration, logic, and data contracts. This document details the internal components that power the public API facades.

Dependencies flow from the high-level public facades, through the orchestrating engine components, down to the specialized logic and core contracts. This layered approach ensures high cohesion, low coupling, and excellent testability.

graph TD

    subgraph Public API Facades

        A\[ApplicationApi\]

        B\[OperationalApi\]

        C\[DataApi\]

    end

    subgraph Internal Engine Components

        D\[MigrationRunner\] \--\> E\[MigrationPlanner\]

        D \--\> F\[ThreeWayMerger\]

        D \--\> G\[SnapshotManager\]

        E \--\> H\[FileDiscoverer\]

        E \--\> I\[MigrationRegistry\]

        F \--\> I

    end

    subgraph Core & Logic

        J\[Migration Implementations\]

        K\[Discovery Rule Implementations\]

        L\[Core DTOs & Contracts\]

    end

    A \--\> D

    B \--\> E

    B \--\> D

    C \--\> D

    H \--\> K

    I \--\> J

    J \--\> L

    K \--\> L

## **2\. Core Module: The Foundation**

This module contains no logic. It is the foundational set of shared contracts, interfaces, and data structures used by all other parts of the system.

* **Responsibilities**: Define the stable, public contracts of the library.  
* **Key Components**:  
  * **IJsonMigration\<TFrom, TTo\>**: The asynchronous, DI-friendly interface that all migration classes must implement.  
  * **Versioned DTOs**: The set of C\# records that define the schema for each document type at each version (e.g., PkgConfV1, PkgConfV2). These are decorated with custom validation attributes.  
  * **Public Data Contracts**: The immutable records used by the public API (VersionedDocument, DocumentBundle, Snapshot, MigrationPlan, MigrationResult, QuarantineRecord).  
  * **Custom Validation Attributes**: The set of attributes used to define validation rules on DTOs (e.g., \[NumberRange\], \[StringPattern\]).

---

## **3\. The Engine: Internal Components**

The Engine is the heart of the library. It is a collection of internal, stateless services that are orchestrated by the public API facades to perform the work.

### **3.1. MigrationRegistry**

* **Responsibilities**: To discover and provide a queryable map of all available migration paths.  
* **Process**:  
  1. On initialization, it is provided with the application's assembly.  
  2. It uses reflection to find all concrete classes that implement IJsonMigration\<TFrom, TTo\>.  
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
* **Inputs**: A C\# Type object (e.g., typeof(PkgConfV2)).  
* **Outputs**: A compiled JsonSchema object.

### **3.3. FileDiscoverer (Implements IDiscoveryService)**

* **Responsibilities**: To translate a MigrationManifest into a verified list of file paths.  
* **Process**:  
  1. Loads the MigrationManifest.json from the highest-precedence location (CLI override \> default \> internal).  
  2. Uses reflection to discover all classes implementing the IDiscoveryRule interface.  
  3. Iterates through the manifest, adding explicit includePaths.  
  4. For each entry in discoveryRules, it finds the matching IDiscoveryRule implementation and calls its Execute() method, passing the rule's parameters.  
  5. Each rule is responsible for performing a "safe peek" on found files to verify the docType before returning its list of verified paths.  
  6. It returns a final, deduplicated list of all verified files.  
* **Inputs**: A MigrationManifest object.  
* **Outputs**: A HashSet\<string\> of absolute file paths.

### **3.4. SnapshotManager**

* **Responsibilities**: To be the sole authority on snapshot file I/O and integrity.  
* **Process (Write)**:  
  1. Receives file content and metadata.  
  2. Calculates the SHA-256 hash of the content.  
  3. Constructs the precise snapshot filename: \<filename\>.\<version\>.\<short\_sha\>.snapshot.json.  
  4. Writes the content to disk.  
* **Process (Read)**:  
  1. Receives a snapshot file path.  
  2. Parses the filename to extract the expected SHA hash.  
  3. Reads the file content and calculates its actual SHA hash.  
  4. If the hashes do not match, it throws a SnapshotIntegrityException. Otherwise, it returns the content.  
* **Inputs**: File content, metadata.  
* **Outputs**: Verified file content, or an exception.

### **3.5. MigrationPlanner**

* **Responsibilities**: To perform a safe, read-only analysis and produce a detailed MigrationPlan.  
* **Process**:  
  1. Takes a collection of DocumentBundle objects as input.  
  2. For each document, it queries the MigrationRegistry to find the required migration path.  
  3. It analyzes the AvailableSnapshots to determine if a STANDARD\_UPGRADE or a THREE\_WAY\_MERGE is required.  
  4. It compiles these findings into a list of PlanAction objects.  
  5. It returns a final MigrationPlan containing the complete list of actions.  
* **Inputs**: IEnumerable\<DocumentBundle\>, target version.  
* **Outputs**: A MigrationPlan object.

### **3.6. ThreeWayMerger**

* **Responsibilities**: To contain the complex, state-sensitive logic for a lossless merge.  
* **Process**:  
  1. Receives the three JObjects: **BASE**, **MINE**, and **THEIRS**.  
  2. Uses the MigrationRegistry to create a "virtual" migrated version of the BASE object to establish a common ancestor for comparison.  
  3. Uses a JSON Patch library (e.g., JsonPatch.Net) to generate two patch documents: diff(THEIRS, virtualBase) and diff(MINE\_migrated, virtualBase).  
  4. Applies the "Theirs" patch first, then attempts to apply the "Mine" patch, enforcing the conflict resolution policy and logging any discarded changes.  
* **Inputs**: Three JObjects.  
* **Outputs**: A single, merged JObject and a list of conflict resolutions.

### **3.7. MigrationRunner**

* **Responsibilities**: To execute a MigrationPlan. This is the primary component that orchestrates writes and state changes.  
* **Process**:  
  1. Takes a MigrationPlan and a collection of DocumentBundle objects as input.  
  2. For each PlanAction, it executes the required workflow in a try...catch block.  
  3. **For an upgrade**: It calls SnapshotManager to create a snapshot, then runs the migration chain, and prepares the final DataMigrationResult.  
  4. **For a merge**: It calls SnapshotManager to verify the input snapshots, delegates the core logic to the ThreeWayMerger, and prepares the DataMigrationResult, including a list of snapshots to be deleted.  
  5. **On failure**: The catch block creates a detailed QuarantineRecord and adds it to a list of failed operations.  
  6. After processing all actions, it compiles and returns a final MigrationResult object containing lists of all successful and failed operations.  
* **Inputs**: MigrationPlan, IEnumerable\<DocumentBundle\>.  
* **Outputs**: A MigrationResult object.

---

## **5\. Data Flow & Orchestration Example: File-Based Downgrade**

This shows how the IOperationalApi acts as a thin wrapper over the internal components.

**Scenario**: IOperationalApi.ExecutePlanAgainstFileSystemAsync(plan) is called.

1. **OperationalApi**: Receives the MigrationPlan. It begins iterating through the PlanAction items. For each action:  
2. **OperationalApi**: It reads the source file and all its associated snapshot files from disk.  
3. **OperationalApi**: It constructs the DocumentBundle object in memory from the file content.  
4. **MigrationRunner**: The OperationalApi now calls the MigrationRunner's internal ExecuteAction method, passing this single DocumentBundle and its PlanAction.  
5. **MigrationRunner**: Executes the full downgrade algorithm, creating snapshots via SnapshotManager and running the reverse migration. It returns a DataMigrationResult (or throws a MigrationQuarantineException).  
6. **OperationalApi**: Receives the result. It then performs the necessary file I/O:  
   * Overwrites the source file with the new data.  
   * Creates new snapshot files.  
   * Deletes obsolete snapshot files.  
   * In case of an exception, it writes the QuarantineRecord to a file.  
7. **OperationalApi**: After processing all actions, it aggregates the individual results into a final MigrationResult and returns it.

This design ensures that each component has a clear, testable, and limited set of responsibilities, creating a system that is robust, maintainable, and highly reusable.