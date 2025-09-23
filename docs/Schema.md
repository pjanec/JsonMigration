# **Schema Definition and Validation Explained**

## **1\. The "Code-First" Philosophy**

In the MigrationSystem library, the **C\# code is the single source of truth**. Instead of maintaining separate, language-agnostic .schema.json files, we define the structure and rules for our data directly on the C\# Data Transfer Objects (DTOs).

**Why this approach?**

* **Maintainability**: It eliminates the risk of a schema file becoming out-of-sync with the application code that uses it. When you refactor a DTO property, you are refactoring the schema at the same time.  
* **Simplicity**: Developers only need to look in one place—the DTO file—to understand the complete definition of a data structure, including its validation rules.  
* **Safety**: It combines the compile-time safety of C\# (correct property names, correct data types) with powerful, explicit runtime validation (value ranges, string patterns).

## **2\. How a Schema is Defined (The Developer's Role)**

A schema is defined in two parts: its **structure** and its **constraints**.

### **2.1. Defining Structure with DTOs**

The basic shape of your data is defined by the properties of your C\# record or class. The schema generator recursively scans this structure to understand property names, data types (int, string, List\<\>, nested objects, etc.), and which fields are required.

**Example: A Simple DTO Structure**

public sealed record ReportingConfigV2  
{  
    // The generator knows this is a required string property named "format"  
    public string Format { get; init; }  
}

public sealed record PkgConfV2  
{  
    public int ExecutionTimeout { get; init; }

    // The generator understands this is a nested object  
    public ReportingConfigV2 Reporting { get; init; }  
}

### **2.2. Defining Constraints with Attributes**

To add validation rules beyond simple structure, you "decorate" your DTO properties with custom attributes.

**Example: A DTO with Validation Attributes**

using MigrationSystem.Core.Validation;  
using Newtonsoft.Json;

public sealed record PkgConfV2  
{  
    \[SchemaDescription("The maximum execution time in seconds.")\]  
    \[NumberRange(1, 300)\] // Value must be between 1 and 300  
    public int ExecutionTimeout { get; init; }

    \[JsonProperty(Required \= Required.Always)\] // Built-in check for presence  
    public ReportingConfigV2 Reporting { get; init; }  
}

public sealed record ReportingConfigV2  
{  
    \[SchemaDescription("The output format for reports.")\]  
    \[StringPattern("^(json|xml|csv)$")\] // Value must match this regex  
    public string Format { get; init; }  
}
