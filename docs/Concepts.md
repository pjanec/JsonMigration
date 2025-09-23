# **Transparent Versioning & Migration System \- The Complete Design**

## **1\. Introduction & Core Philosophy**

### **1.1. The Problem**

Modern applications often rely on structured JSON documents for configuration, user data, or project files. As an application evolves, its data schemas must also evolve. Managing these changes—especially across different installed versions of an application—is complex and fraught with risk. A naive approach can lead to data loss, application instability, and a poor user experience during upgrades or downgrades.

### **1.2. The Solution**

This document specifies the design for a reusable .NET library that provides a robust, transparent, and safe system for managing the versioning and migration of JSON-based data.

The core philosophy is to **decouple the application's business logic from the complexities of data versioning**. The host application will always operate on its latest, well-defined schema, while the migration library handles all the necessary transformations in the background.

### **1.3. Key Goals**

* **Safety**: No operation should ever result in the loss of user-generated data.  
* **Transparency**: The host application's code should be unaware of migration details.  
* **Reusability**: The system must be a generic library, capable of handling data from files, databases, or any other source.  
* **Operability**: The system must provide clear, actionable feedback and tools for developers, CI/CD pipelines, and support engineers.

## **2\. Core Concepts & Foundational Policies**

These are the fundamental rules and concepts upon which the entire system is built.

### **2.1. Schema Versioning**

All versioned documents must contain a \_meta block.

* **Schema Version**: MAJOR.MINOR format (e.g., "1.0", "2.1").  
  * **MAJOR**: Incremented for breaking, non-backward-compatible changes. An application is designed to natively understand one MAJOR version at a time.  
  * **MINOR**: Incremented for additive, backward-compatible changes.  
* **Document Type**: A "magic string" (e.g., "PkgConf", "UserProfile") that identifies the type of data, allowing the system to select the correct migration logic.

### **2.2. The Snapshot: A Guarantee of Integrity**

Snapshots are the cornerstone of rollback safety. They are verifiable backups of a document's state at a critical moment.

* **Purpose**: To preserve the state of a document before a destructive write operation (upgrade or rollback).  
* **Integrity Guarantee**: Every snapshot is verified using a **SHA-256 content hash**. The engine will refuse to use a snapshot if its content does not match the hash, preventing data corruption from incomplete or tampered files.  
* **Lifecycle**: Snapshots are created before state-changing operations and are safely removed by a dedicated Garbage Collector (GC) command once they are obsolete.  
* **Storage**:  
  * **File-Based**: Stored alongside the source file with the naming convention: \<original\_filename\>.\<version\>.\<short\_sha256\>.snapshot.json.  
  * **Non-File-Based**: The host application is responsible for persisting and retrieving Snapshot objects via the API.

### **2.3. The Conflict Resolution Policy**

During a three-state merge (a re-upgrade after a rollback), the system must have a deterministic way to handle conflicts.

* **The Rule**: If the same field was edited in both the rolled-back version ("Mine") and the newer version's snapshot ("Theirs"), **the change from the newer version's snapshot ("Theirs") always wins.**  
* **Rationale**: This policy preserves the user's latest and most relevant work, which was performed in the more feature-rich version of the application. The losing edit is logged for auditing.

### **2.4. The Migration Manifest: Flexible File Discovery**

The system discovers which files to manage based on a MigrationManifest.json file. This provides a flexible bridge between the application's knowledge and the user's environment.

* **Hybrid Model**: The final list of files is generated from a combination of the application's hardcoded internal rules and an optional, user-configurable external manifest.  
* **Structure**: The manifest supports both explicitly listed paths (includePaths) and named, parameter-driven discovery rules (discoveryRules).  
* **Verification**: All discovery rules must specify an expected docType. The engine will perform a "safe peek" on every found file to verify its \_meta.doc\_type before considering it a managed file.  
* **CLI Override**: The path to the manifest can be specified on the command line, overriding the default location for maximum operational flexibility.

## **3\. System Architecture & Public API**

The library is designed with a clean, layered architecture, exposing a set of single-responsibility services through a main facade.

### **3.1. Initialization: The MigrationSystemBuilder**

A consuming application configures and initializes the system using a fluent builder.

IMigrationSystem migrationSystem \= new MigrationSystemBuilder()  
    .WithMigrationsFromAssembly(typeof(MyApp.Program).Assembly)  
    .WithDefaultManifestPath("...")  
    .WithQuarantineDirectory("...")  
    .Build();

### **3.2. The Main Facade: IMigrationSystem**

This is the single entry point to all the library's functionality, exposing a set of specialized facades.

public interface IMigrationSystem  
{  
    // \--- HIGH-LEVEL SERVICES (for most app developers) \---  
    IApplicationApi Application { get; } // For single-file operations  
    IDataApi Data { get; }             // For single non-file operations

    // \--- LOW-LEVEL SERVICES (for installers, CI/CD, and advanced apps) \---  
    IDiscoveryService Discovery { get; }      // Standalone file discovery  
    IOperationalDataApi OperationalData { get; } // Core non-file batch engine  
    IOperationalApi Operations { get; }      // File-based batch convenience layer  
}

### **3.3. Core Contracts**

#### **IJsonMigration\<TFrom, TTo\>**

This is the interface developers implement for each migration step. It is designed to be asynchronous and dependency-injection friendly to handle complex, real-world logic.

public interface IJsonMigration\<TFrom, TTo\> where TFrom : class where TTo : class  
{  
    Task\<TTo\> ApplyAsync(TFrom fromDto);  
    Task\<TFrom\> ReverseAsync(TTo toDto);  
}

// Example: DI-friendly migration class  
public class MyMigration : IJsonMigration\<DocV1, DocV2\>  
{  
    private readonly IMyService \_myService;  
    public MyMigration(IMyService myService) { \_myService \= myService; }  
    // ... implementation  
}

#### **Public Data Contracts**

The API uses a set of immutable record types for all data exchange: VersionedDocument, DocumentBundle, Snapshot, MigrationPlan, MigrationResult, QuarantineRecord.

### **3.4. Code-First Schema Validation**

To ensure the code remains the single source of truth, validation rules are defined directly on the C\# DTOs using custom attributes. The library generates the formal JSON Schema from these DTOs on-the-fly.

public sealed record MyDtoV2  
{  
    \[NumberRange(1, 300)\]  
    public int Timeout { get; init; }

    \[StringPattern("^(json|xml|csv)$")\]  
    public string Format { get; init; }  
}

## **4\. Key Workflows & Algorithms**

### **4.1. The Two-Phase Workflow: Plan → Execute**

All batch operations are designed around this fundamental safety principle.

1. **Plan (Dry Run)**: The Plan\* methods are **read-only**. They analyze the current state of the data (from files or memory) and produce a detailed, machine-readable MigrationPlan object. This plan is the blueprint for the operation.  
2. **Execute**: The Execute\* methods take a MigrationPlan object as input and perform the actual, state-changing work. The execution phase trusts the plan and does not re-analyze the data.

This workflow is critical for both manual operator safety checks and automated CI/CD approval gates.

### **4.2. The Three-State Merge Algorithm (Lossless Re-Upgrade)**

This is the core algorithm for preserving user data after a rollback.

1. **Discover**: The engine detects a document that needs to be upgraded and finds snapshots from a previous rollback.  
2. **Identify States**: It identifies the three necessary inputs:  
   * **BASE**: The pre-upgrade snapshot from the original version. The common ancestor.  
   * **MINE**: The current document on disk, which may have been edited after the rollback.  
   * **THEIRS**: The pre-rollback snapshot from the newer version, containing the user's work.  
3. **Merge**: Using a JSON Patch library, the engine calculates the diffs between (MINE, BASE) and (THEIRS, BASE). It applies these patches to a clean copy of the BASE, enforcing the conflict resolution policy (Theirs \> Mine).

### **4.3. The Installer Downgrade Workflow**

This workflow enables an installer for an older version of an application to safely downgrade data created by a newer version.

1. **Installer Detects**: The installer for App v1.0 detects that App v2.0 is currently installed.  
2. **Installer Calls CLI**: The installer executes a command on the **still-installed v2.0 CLI** (e.g., reverse-migrate-all \--target-version 1.0).  
3. **v2.0 Engine Works**: The v2.0 application uses its IOperationalApi to discover all managed files, plan the rollback, and execute it, creating pre-rollback snapshots along the way.  
4. **Installer Proceeds**: On success (exit code 0), the installer removes the v2.0 binaries and installs the v1.0 binaries. On failure, it **aborts**, leaving the system in a consistent v2.0 state.

### **4.4. Snapshot Garbage Collection Algorithm**

The migrate-gc command safely removes obsolete snapshots.

* **Rule**: A snapshot is considered obsolete and can be deleted if its version does not match the version of the current "live" document, *unless* the snapshot's version is higher than the live document's version.  
* **Safety Net**: This rule carefully preserves the critical pre-rollback snapshot (e.g., a .v2.0.snapshot) when the live file has been successfully rolled back to v1.0.

## **5\. Error Handling & Reporting**

The system provides clear, structured, and actionable reports for both success and failure.

### **5.1. The Quarantine Mechanism**

When an unrecoverable error occurs for a single document, it is quarantined.

* **File-Based**: The problematic source file is moved to a quarantine directory, and a detailed .quarantine.json report is generated.  
* **Non-File-Based**: An operation will throw a MigrationQuarantineException. This rich exception object contains the original data and a QuarantineRecord with detailed diagnostics, allowing the host application to implement its own dead-letter queue or other error-handling logic.

### **5.2. The Migration Result**

Every batch operation produces a MigrationResult object.

* **Dual Purpose**: This object provides a high-level summary for human operators and a detailed, machine-readable list of all successful and failed operations.  
* **Smart Retries**: The MigrationResult from a failed run can be used as the input for a subsequent "retry" command. The system will then attempt to migrate only the files that previously failed, making operations more efficient and resilient.