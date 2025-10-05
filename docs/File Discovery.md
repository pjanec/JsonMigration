# **File Discovery & The Migration Manifest Explained**

## **1. Why is Discovery Necessary?**

A reusable migration library cannot make assumptions about where an application stores its data. User project files might be in C:\\MyProjects\\, application configuration could be in %APPDATA%, and shared templates might live on a network drive. The discovery system provides a flexible and explicit way for an operator to tell the migration engine, **"These are the files you are responsible for."**

This is achieved through a central configuration file: the **MigrationManifest.json**.

## **2. The MigrationManifest.json File**

The manifest is a JSON file that acts as the single source of truth for all file-based operations. It defines the scope of any batch command, like plan-upgrade or migrate-gc.

### **When is it used?**

It is loaded and parsed at the beginning of any operational command that needs to work with a collection of files.

### **The Hybrid Model**

The system is designed to combine two sources to build its final list of files:

1. **The External Manifest**: The MigrationManifest.json file, which is ideal for user-configurable locations (e.g., project directories).  
2. **The Internal Manifest**: The host application can also have hardcoded, internal rules for its own configuration files whose locations it always knows (e.g., user\_preferences.json).

The engine combines the rules from both sources to create a complete list.

### **Operational Flexibility**

The location of the manifest is resolved with a clear order of precedence, providing control for different environments:

1. **CLI Override (\--manifest flag)**: A path provided on the command line is used exclusively. Perfect for CI/CD or special operational tasks.  
2. **Default External Location**: If no override is given, the engine looks for the manifest in a well-known application data directory.  
3. **Internal Only**: If neither of the above is found, the engine proceeds using only the application's internal, hardcoded rules.

## **3. Structure of the Manifest**

The manifest is designed to be both simple and powerful, supporting two primary ways of defining the file scope.

**Example MigrationManifest.json:**

{  
  "includePaths": \[  
    "C:/Users/User/AppData/Roaming/MyApp/user\_preferences.json"  
  \],  
  "discoveryRules": \[  
    {  
      "ruleName": "MyApp\_Project\_Files",  
      "parameters": {  
        "rootPath": "C:/Work/Projects/",  
        "fileMask": "\*.myproj",  
        "docType": "MyProjectFile",  
        "maxDepth": 5  
      }  
    }  
  \]  
}

## **4. Relationship to Schema Version Config**

The MigrationManifest.json and schema\_versions.json files work together to provide complete control over batch migrations:

* The **Migration Manifest** answers the question: "**Which files** should be managed?"
* The **Schema Version Config** answers the question: "**What version** should each of those files be?"

An installer or operator will typically use the manifest to discover all relevant files and then use the schema config to plan and execute a migration that brings every discovered file to its required version.

### **4.1. Workflow Integration**

```csharp
// 1. Discover files using the manifest
var manifest = await discoveryService.LoadManifestAsync("manifest.json");
var managedFiles = await discoveryService.DiscoverManagedFilesAsync(manifest);

// 2. Generate schema config for target versions
await operationalApi.WriteSchemaConfigAsync("schema_versions.json");

// 3. Plan multi-version migration using both
var plan = await operationalApi.PlanFromConfigAsync("schema_versions.json", "manifest.json");

// 4. Execute with resilient transactions
var result = await operationalApi.ExecutePlanAgainstFileSystemAsync(plan, "transactions/");
```

This separation of concerns allows for flexible deployment scenarios where the discovery rules can be customized for different environments while maintaining consistent versioning policies through the schema configuration.
