# **Migration System \- Merge Algorithms Explained**

## **1\. Introduction**

The MigrationSystem library uses two distinct algorithms to handle data transformations. The choice of algorithm is not manual; it is selected automatically by the MigrationPlanner and executed by the MigrationRunner based on the context of the operation. This ensures that the system is both fast for simple, everyday tasks and robustly safe for complex, high-risk scenarios like recovering from a rollback.

The two algorithms are:

1. **The Simple Two-Way Migration**: The standard, fast path for everyday upgrades and downgrades.  
2. **The Three-State Merge**: The safe path, used exclusively to prevent data loss during a re-upgrade after a rollback.

## **2\. Algorithm 1: The Simple Two-Way Migration (The Fast Path)**

This is the workhorse of the migration system. It is a direct, stateless transformation between two schema versions.

### **When is it used?**

* During a **standard, first-time upgrade** (e.g., v1.0 → v2.0).  
* During a **standard downgrade/rollback** (e.g., v2.0 → v1.0).  
* When the MigrationPlanner finds no evidence of a previous rollback cycle (i.e., no conflicting snapshots).

### **Why is it used?**

It is the simplest, fastest, and most efficient method for transforming data when there is no complex history of version changes to consider.

### **How it Works: A Concrete Example**

Imagine upgrading a V1 document.

**Input (config.v1.json):**

{

  "\_meta": { "DocType": "PkgConf", "SchemaVersion": "1.0" },

  "timeout": 30,

  "plugins": \["auth", "logging"\]

}

**The Process (Forward Migration):**

1. **Create Snapshot**: The engine creates a verifiable snapshot containing the exact V1 content.  
2. **Load V1 DTO**: The JSON is deserialized into a PkgConfV1 object.  
3. **Transform**: The ApplyAsync() method of the Migrate\_PkgConf\_1\_0\_To\_2\_0 class is called. It renames timeout, converts the plugins array to a dictionary, and adds a default reporting object.  
4. **Receive V2 DTO**: A new PkgConfV2 object is returned.  
5. **Save V2 DTO**: The V2 DTO is serialized, atomically overwriting the original file.

**Output (config.v2.json):**

{

  "\_meta": { "DocType": "PkgConf", "SchemaVersion": "2.0" },

  "execution\_timeout": 30,

  "plugins": {

    "auth": { "enabled": true },

    "logging": { "enabled": true }

  },

  "reporting": { "format": "json" }

}

## **3\. Algorithm 2: The Three-State Merge (The Safe Path)**

This is the most powerful algorithm, designed solely to **prevent the loss of user data** after a rollback-and-re-upgrade cycle.

### **When is it used?**

It is used **exclusively and automatically** for a forward migration when the MigrationPlanner detects snapshots from a previous, completed rollback.

### **Why is it used?**

A simple two-way migration would be destructive in this scenario. It would blindly re-apply the V1-to-V2 transformation, destroying any edits the user made in their previous V2 session. The three-state merge uses the historical snapshots to intelligently combine changes.

### **The Inputs: A Detailed Example**

To perform the merge, the engine gathers three distinct VersionedDocument inputs.

1\. BASE (The Common Ancestor)

The state of the document before the very first upgrade.

{

  "\_meta": { "DocType": "PkgConf", "SchemaVersion": "1.0" },

  "timeout": 30,

  "plugins": \["auth", "logging"\]

}

2\. MINE (The Local Version)

The current V1 document, which the user edited after the rollback. They changed timeout to 45 and removed the "auth" plugin.

{

  "\_meta": { "DocType": "PkgConf", "SchemaVersion": "1.0" },

  "timeout": 45,

  "plugins": \["logging"\]

}

3\. THEIRS (The Remote Version)

The V2 snapshot, containing all the user's work from their previous V2 session. They changed execution\_timeout to 100, disabled "logging", and added a "cache" plugin.

{

  "\_meta": { "DocType": "PkgConf", "SchemaVersion": "2.0" },

  "execution\_timeout": 100,

  "plugins": {

    "auth": { "enabled": true },

    "logging": { "enabled": false },

    "cache": { "enabled": true }

  },

  "reporting": { "format": "json" }

}

### **The Merge Algorithm in Detail**

1. **Establish Common Ground**: The BASE (V1) object is migrated in-memory to V2. This creates a "virtual base V2" that looks like the simple upgrade output from Algorithm 1\. This is our clean slate for comparison.  
2. **Generate Diffs (Patches)**: The engine calculates two sets of changes:  
   * **"Theirs" Patch**: A diff between the **THEIRS** snapshot and the **virtual base V2**. This patch represents the user's work in V2.  
     * replace /execution\_timeout with 100  
     * replace /plugins/logging/enabled with false  
     * add /plugins/cache with { "enabled": true }  
   * **"Mine" Patch**: A diff between the **MINE** document (also migrated in-memory to V2) and the **virtual base V2**. This represents the user's work post-rollback.  
     * replace /execution\_timeout with 45  
     * remove /plugins/auth  
3. Apply Patches & Resolve Conflicts: The engine applies these patches to a fresh copy of the virtual base V2.  
   a. First, it applies the "Theirs" Patch in its entirety. The document now reflects all the V2 edits.  
   b. Second, it intelligently applies the "Mine" Patch.  
   \- It tries to apply remove /plugins/auth. This path was not changed by "Theirs", so the change is accepted. The "auth" plugin is removed.  
   \- It tries to apply replace /execution\_timeout with 45\. This path was changed by "Theirs". This is a conflict. According to the policy, the "Theirs" change wins. The "Mine" operation is discarded, and the conflict is logged.

### **The Final Merged Output**

The final, merged JObject is then serialized to disk.

{

  "\_meta": { "DocType": "PkgConf", "SchemaVersion": "2.0" },

  "execution\_timeout": 100,

  "plugins": {

    "logging": { "enabled": false },

    "cache": { "enabled": true }

  },

  "reporting": { "format": "json" }

}

**Analysis of the Result:**

* execution\_timeout is **100**: The "Theirs" change won the conflict. **Data preserved.**  
* plugins.cache is present: The non-conflicting addition from "Theirs" was applied. **Data preserved.**  
* plugins.logging is disabled: The non-conflicting change from "Theirs" was applied. **Data preserved.**  
* plugins.auth is absent: The non-conflicting removal from "Mine" was applied. **User intent preserved.**

This process safely restores the user's work from the newer V2 session while preserving any compatible, non-conflicting changes they made after the rollback.
