# Merging

This document covers the most critical feature of MigrationSystem: the **three-state merge algorithm**. This is the engine's foundational logic for preventing user data loss during complex operations like a rollback followed by a re-upgrade.

## **1. Overview**

In a traditional JSON migration system, each migration is a one-way trip. The system moves a document from Version A to Version B and does not preserve the original state. This works fine for simple migrations, but it creates a serious data loss problem when rollbacks are involved.

Consider this sequence:

1. Upgrade: V1 → V2  
2. User edits the V2 document  
3. Rollback: V2 → V1  
4. User edits the V1 document  
5. Re-upgrade: V1 → V2  

In step 5, a naive migration system would blindly re-apply the V1-to-V2 transformation, destroying all the user's work from step 2. MigrationSystem prevents this by using a sophisticated three-state merge algorithm.

## **2. Algorithm 1: Standard Forward Migration (The Simple Path)**

This is used when no rollback history is detected. The system simply applies the migration transformations from the current version to the target version.

**Example**: A V1 document with `timeout: 30` and `plugins: ["auth", "logging"]` becomes a V2 document with `execution_timeout: 30` and `plugins: {"auth": {"enabled": true}, "logging": {"enabled": true}}`.

## **3. Algorithm 2: The Hybrid Three-Way Merge (The Safe Path)**

This is the most powerful algorithm, designed solely to **prevent the loss of user data** after a rollback-and-re-upgrade cycle. It uses a sophisticated hybrid strategy that combines the precision of domain-specific "semantic handlers" with the power of a formal, structural merge algorithm.

### **When is it used?**

It is used **exclusively and automatically** for a forward migration when the MigrationPlanner detects snapshots from a previous, completed rollback.

### **The Hybrid Merge Algorithm in Detail**

The engine intelligently processes each property in the document, choosing the best tool for the job.

1. **Semantic Handler Priority**: The merger first iterates through every property of the document. For each one, it checks if the migration class provides a custom **semantic handler** for that specific property (by implementing the ISemanticMigration interface).
   * **If a handler exists** (as it does for plugins), the merger completely delegates the merging of that single property to the handler's custom logic. The handler is trusted to produce the correct, semantically-aware result.
   * This ensures that fundamental transformations (like a List changing to a Dictionary) are handled with perfect, domain-specific accuracy.

2. **Formal Structural Merge**: After all semantically-handled properties are processed and set aside, the engine performs a formal, patch-based merge on **all remaining properties** (Execution_timeout, Reporting, etc.).
   * **Generate Diffs (Patches)**: The engine calculates two sets of changes using a JSON Patch library:
     * **"Theirs" Patch**: A diff between the **THEIRS** snapshot and the **BASE** document.
     * **"Mine" Patch**: A diff between the **MINE** document and the **BASE** document.
   * **Merge Patches**: The engine creates a new, final patch by combining the operations from both patches. If a conflict occurs (i.e., the same property path was changed in both Mine and Theirs), it is resolved using the "Mine Wins" policy.
   * **Apply Final Patch**: This single, merged patch is applied to the **BASE** document to produce the final state for all structurally-merged properties.

3. **Combine Results**: The final document is constructed by combining the results from the semantic handlers with the results from the formal structural merge.

### **Analysis of the Result:**

This hybrid process correctly resolves our test case:

* Plugins is merged by its **semantic handler**, which correctly understands that "auth" was removed in MINE.
* Execution_timeout is merged by the **formal algorithm**. It detects a conflict and resolves it using the "Mine Wins" policy, resulting in 45.
* Reporting is merged by the **formal algorithm**. It sees the property was only added in THEIRS and applies the change without conflict.

This hybrid process safely restores the user's work while preserving all compatible, non-conflicting changes they made after the rollback.

## **4. Conflict Resolution Policies**

When both "Mine" and "Theirs" modify the same property, the system follows a conflict resolution policy:

- **Mine Wins** (Default): The local changes take precedence
- **Theirs Wins**: The remote/snapshot changes take precedence

The policy can be configured per migration class through the semantic handler interface.

## **5. Implementation Details**

The three-state merge is implemented in the `ThreeWayMerger` class, which:

- Maintains backward compatibility with existing APIs
- Supports the `ISemanticMigration` interface for domain-specific logic
- Falls back to robust structural merging for general cases
- Provides comprehensive error handling and graceful degradation

For technical implementation details, see the [Modules](Modules.md) documentation.
