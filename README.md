# **1\. Overview**

## **1.1 Purpose & Core Philosophy**

The MigrationSystem is a reusable .NET library designed to solve the complex problem of managing the lifecycle of versioned JSON data in modern applications.

Its core philosophy is to **decouple an application's business logic from the complexities of data versioning**. This allows the host application to be written as if it only ever interacts with the latest data schema, dramatically simplifying its code. The library transparently handles all the necessary data transformations in the background, ensuring safety, reliability, and a seamless user experience across application versions.

## **1.2 Key Features**

The library is built on a set of powerful, production-ready features designed to handle real-world operational challenges.

* **Transparent In-Memory Migrations**: Applications can load older versions of data (e.g., a v1.0 file) and will transparently receive a fully migrated, up-to-date v2.0 object in memory, without the application code needing to be aware of the transformation.  
* **Lossless, Symmetrical Rollbacks**: The system's most critical feature is its ability to handle a full upgrade -> edit -> rollback -> edit -> re-upgrade cycle without losing any user data. It uses a sophisticated **hybrid three-state merge** algorithm to intelligently combine changes from different versions.  See more details in [Merging](docs/Merging.md) document.
* **Safe, Auditable Batch Operations**: For installers and administrators, all batch operations (like upgrading an entire directory of files) use a safe, two-phase **"Plan → Execute"** workflow. The system first generates a detailed, read-only "dry run" plan that can be reviewed and approved before any changes are committed to disk.  
* **Storage Agnostic**: While providing first-class support for file-based data, the core engine is completely decoupled from the file system. A comprehensive public API allows developers to use the library's powerful migration and merge logic on data stored in **databases, caches, message queues**, or any other backend.  
* **Code-First Schema Validation**: To ensure the application code remains the single source of truth, validation rules (like value ranges or string patterns) are defined directly on C\# DTOs using attributes. The library generates and caches formal JSON Schemas from these DTOs on-the-fly, eliminating the need to maintain separate, error-prone schema files. See more details in [Schema](docs/Schema.md) document. 
* **Flexible File Discovery**: For file-based operations, the system uses a `MigrationManifest.json` file that allows operators to define both explicit file paths and powerful, rule-based discovery to automatically find and manage all relevant data files.  See [File Discovery](docs/File Discovery.md) document for more details.
* **Robust Error Handling**: When an unrecoverable error occurs, the system doesn't just fail; it **quarantines** the problematic data. It produces a rich, structured diagnostic report that gives operators clear, actionable information to resolve the issue.

## **1.3 Core Design Principles**

The library's features are enabled by a set of robust architectural decisions.

* **Layered & Decoupled Architecture**: The system is composed of distinct layers: high-level facades for consumers, an internal engine for orchestration, and a core set of contracts. This separation of concerns makes the library highly testable and maintainable.  
* **Dependency Injection First**: The entire system is designed to be configured and used via a standard .NET dependency injection container, making it easy to integrate into any modern application.  
* **Integrity via Hashing**: All snapshots, which are the key to rollback safety, have their filenames enriched with a **SHA-256 hash** of their content. The engine verifies this hash every time a snapshot is read, guaranteeing that migrations are never performed with corrupt or tampered data.  
* **Stateless Core Logic**: The internal engine components are stateless, receiving all necessary information through their method calls. This makes the core logic predictable, thread-safe, and easy to reason about. See more details in [Modules](docs/Modules.md) document.

This design results in a powerful, flexible, and exceptionally safe library that provides a comprehensive solution to the challenges of data schema evolution.

See more details in [Concepts](docs/Concepts.md) document.

<br>

The guide below demonstrates how to use the MigrationSystem library to solve common versioning and migration challenges. It provides practical, code-first examples for three primary developer personas:

1. **The GUI Application Developer**: Working with single, user-facing files.  
2. **The Installer & CI/CD Engineer**: Performing safe, automated batch operations.  
3. **The Backend Developer**: Managing versioned data from a non-file source like a database.

# **2\. Initial Setup & Configuration**

Before using the library, it must be configured with your application's specific migration logic and registered in your application's service container.

## **2.1. Implement a Migration**

First, define your DTOs using init properties for immutability. Then, create a class that implements IJsonMigration for each step.

```csharp
// --- DTOs ---  
public record PkgConfV1  
{  
    public int Timeout { get; init; }  
    public List<string> Plugins { get; init; } = new();  
}

public record PkgConfV2  
{  
    public int ExecutionTimeout { get; init; }  
    public Dictionary<string, object> Plugins { get; init; } = new();  
}
```

```csharp
// --- Migration Logic ---  
public class Migrate_PkgConf_1_0_To_2_0 : IJsonMigration<PkgConfV1, PkgConfV2>  
{  
    public Task<PkgConfV2> ApplyAsync(PkgConfV1 fromDto)  
    {  
        var toDto = new PkgConfV2   
        {   
            ExecutionTimeout = fromDto.Timeout,  
            Plugins = fromDto.Plugins.ToDictionary(p => p, _ => new { enabled = true })  
        };  
        return Task.FromResult(toDto);  
    }

    public Task<PkgConfV1> ReverseAsync(PkgConfV2 toDto)  
    {  
        var v1Dto = new PkgConfV1   
        {   
            Timeout = toDto.ExecutionTimeout,  
            Plugins = toDto.Plugins.Keys.ToList()  
        };  
        return Task.FromResult(v1Dto);  
    }  
}
```

## **2.2. Register in Dependency Injection**

In your application's startup code (e.g., Program.cs), use the `AddMigrationSystem` extension method to register the library and its services.

```csharp
// In your Program.cs or other startup configuration  
builder.Services.AddMigrationSystem(options =>  
{  
    // Scan your main application assembly for IJsonMigration implementations  
    options.WithMigrationsFromAssembly(typeof(Program).Assembly);

    // Set the default location for the manifest and quarantine directory  
    options.WithDefaultManifestPath("path/to/your/MigrationManifest.json");  
    options.WithQuarantineDirectory("path/to/your/quarantine");  
});
```

# **3\. The GUI Application Developer: Single-File Operations**

**Use Case**: A desktop or web application that needs to open, edit, and save a versioned document.

## **Scenario 3.1: Loading and Displaying a Document**

The `IApplicationApi` makes loading files transparent. Your application code will always receive the latest DTO, even if the file on disk is an older version.

```csharp
public class EditorViewModel  
{  
    private readonly IApplicationApi _appApi;  
    public PkgConfV2 CurrentConfig { get; private set; }

    // Inject the IMigrationSystem facade, then access its Application property  
    public EditorViewModel(IMigrationSystem migrationSystem)  
    {  
        _appApi = migrationSystem.Application;  
    }
    
    public async Task LoadDocument(string filePath)  
    {  
        try  
        {  
            // Load the file. The library handles any necessary in-memory migration.  
            var result = await _appApi.LoadLatestAsync<PkgConfV2>(filePath, validate: true);
    
            this.CurrentConfig = result.Document;
    
            if (result.WasMigrated)  
            {  
                // Optionally, inform the user that their file format was updated  
                // in memory to be compatible with this version of the app.  
                Console.WriteLine("File was migrated in-memory to the latest version.");  
            }  
        }  
        catch (Exception ex) // Catch a general exception for simplicity  
        {  
            // The file might be corrupt, fail validation, or fail migration.  
            // In a real app, you would inspect the exception type.  
            ShowErrorDialog("Could not load file", ex.Message);  
        }  
    }  
}
```

## **Scenario 3.2: Saving a Document (Migrate on Disk)**

When you want to not only load a file but also automatically upgrade it on disk, use the `MigrateOnDisk` behavior. This performs a safe, snapshot-based write.

```csharp
public class EditorService  
{  
    private readonly IApplicationApi _appApi;  
    // ...

    public async Task OpenAndUpgradeDocument(string filePath)  
    {  
        // This single call will:  
        // 1\. Read the old file.  
        // 2\. Perform an in-memory migration.  
        // 3\. Create a pre-upgrade snapshot for safety.  
        // 4\. Atomically overwrite the original file with the new, migrated content.  
        var result = await _appApi.LoadLatestAsync<PkgConfV2>(filePath, LoadBehavior.MigrateOnDisk);  
        // ... now use result.Document  
    }
    
    public async Task SaveDocument(string filePath, PkgConfV2 document)  
    {  
        // SaveLatestAsync always writes the document using the latest schema.  
        await _appApi.SaveLatestAsync(filePath, document);  
    }  
}
```

# **4\. The Installer & CI/CD Engineer: Batch Operations**

**Use Case**: An installer needs to safely downgrade all application data, or a CI/CD pipeline needs to run a pre-deployment migration.

## **Scenario 4.1: Performing a Safe, Auditable Upgrade**

The two-phase "Plan → Execute" workflow is essential for safety and automation.

```csharp
public class DeploymentScript  
{  
    private readonly IOperationalApi _opsApi;  
    // ...

    public async Task RunAutomatedUpgrade()  
    {  
        // --- PHASE 1: PLAN (Dry Run) ---  
        // This is a safe, read-only operation. The manifest path is optional.  
        Console.WriteLine("Planning migration...");  
        var plan = await _opsApi.PlanUpgradeFromManifestAsync();  
        Console.WriteLine($"Plan created: {plan.Actions.Count} actions to perform.");  
        // In a real CI/CD pipeline, you would save this plan as an artifact  
        // and pause for a manual approval gate.
    
        // --- PHASE 2: EXECUTE ---  
        // After approval, execute the exact plan that was reviewed.  
        Console.WriteLine("Executing migration plan...");  
        var result = await _opsApi.ExecutePlanAgainstFileSystemAsync(plan);
    
        // --- PHASE 3: VERIFY ---  
        Console.WriteLine($"Migration complete. Status: {result.Summary.Status}");  
        if (result.Summary.Failed > 0\)  
        {  
            Console.WriteLine("Migration completed with errors. Check result file for details.");  
            // In a CI/CD pipeline, this would fail the deployment.  
        }  
    }  
}
```

## **Scenario 4.2: Handling Batch Failures (Smart Retry)**

If a batch operation fails due to transient issues, you can use the MigrationResult to retry only the failed items.

```csharp
public async Task HandleFailures(MigrationResult initialResult, IOperationalApi opsApi)  
{  
    if (initialResult.Summary.Failed == 0\) return;

    Console.WriteLine("Initial migration failed. Waiting and retrying failed files...");  
    await Task.Delay(TimeSpan.FromSeconds(5)); // Wait for transient issues to resolve
    
    // The retry method uses the previous result to build a new plan  
    // containing only the previously failed files.  
    var retryResult = await opsApi.RetryFailedFileSystemAsync(initialResult);
    
    Console.WriteLine($"Retry complete. Status: {retryResult.Summary.Status}");  
}
```

# **5\. The Backend Developer: Non-File Data**

**Use Case**: A backend service that stores versioned documents in a database and needs to manage their lifecycle.

## **Scenario 5.1: Upgrading a Document from a Database**

The application is responsible for storing and retrieving the data and snapshots. The library provides the logic.

```csharp
public class DocumentService  
{  
    private readonly IDataApi _dataApi;  
    private readonly IMyDatabase _myDb; // Your database repository  
    // ...

    public async Task UpgradeDocument(string documentId)  
    {  
        // 1\. Fetch the document and any available snapshots from your database.  
        var document = await _myDb.GetDocumentAsync(documentId);  
        var snapshots = await _myDb.GetSnapshotsAsync(documentId);
    
        try  
        {  
            // 2\. Execute the upgrade. The library handles the logic, including a  
            //    three-state merge if necessary, using the snapshots you provide.  
            var result = await _dataApi.ExecuteUpgradeAsync(  
                document.Data,  
                document.Metadata,  
                snapshots  
            );
    
            // 3\. Commit the results back to your database in a single transaction.  
            await _myDb.ExecuteInTransaction(async () =>  
            {  
                await _myDb.UpdateDocumentAsync(documentId, result.Data, result.NewMetadata);  
                await _myDb.SaveSnapshotsAsync(documentId, result.SnapshotsToPersist);  
                await _myDb.DeleteSnapshotsAsync(documentId, result.SnapshotsToDelete);  
            });  
        }  
        catch (MigrationQuarantineException ex)  
        {  
            // 4\. Handle failures by moving the problematic data to a dead-letter table.  
            await _myDb.MoveToDeadLetterTable(documentId, ex.OriginalData, ex.OriginalMetadata, ex.QuarantineRecord);  
        }  
    }  
} 