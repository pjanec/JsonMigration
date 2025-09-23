# Operational Playbook

## Standard Upgrade Procedure

1. **Plan**: Run the `plan-upgrade` command.
   ```bash
   dotnet run --project MigrationSystem.Cli -- plan-upgrade
   ```
2. **Review**: Inspect the console summary and the generated `plan-*.json` file. Ensure the scope of changes is expected. Get necessary approvals.
3. **Execute**: Run the `migrate` command, providing the approved plan.
   ```bash
   dotnet run --project MigrationSystem.Cli -- migrate --plan plan-20250922192800.json
   ```
4. **Verify**: Check the `MigrationResult-*.json` file. If `Status` is not "Completed", investigate failed files in the quarantine directory.

## Emergency Rollback Procedure

1. **Plan**: Run `plan-rollback` specifying the target version.
   ```bash
   dotnet run --project MigrationSystem.Cli -- plan-rollback --target-version 1.0
   ```
2. **Review**: Review the plan. Ensure all critical data will be rolled back.
3. **Execute**: Run `reverse-migrate` with the plan.
4. **Verify**: Check the result file and application functionality. For any quarantined files, create a support ticket.

## Migration System Architecture

### Core Components

- **MigrationSystem.Core**: Defines all public interfaces and data contracts
- **MigrationSystem.Engine**: Implements the migration logic and orchestration
- **MigrationSystem.Cli**: Command-line interface for operational tasks
- **MigrationSystem.TestHelpers**: Utilities for testing migrations

### Key Concepts

- **MetaBlock**: Contains document type and schema version information
- **Snapshot**: Verifiable backup of document state at specific version
- **Migration Plan**: Detailed roadmap of actions to be performed
- **Three-Way Merge**: Lossless merge strategy for re-upgrade scenarios

### File Structure

```
your-project/
??? MigrationManifest.json     # Defines discovery rules
??? data/                      # Your versioned documents
?   ??? config.json           # Document with _meta block
?   ??? config.json.v1.0.abc123.snapshot.json  # Version snapshot
??? quarantine/               # Failed migrations
    ??? failed-doc.json       # Documents requiring manual intervention
```

## Troubleshooting

### Common Issues

1. **Major Version Mismatch**
   - **Symptom**: Documents quarantined with "Major version mismatch"
   - **Solution**: Review migration strategy. Major version changes require manual intervention.

2. **Schema Validation Failures**
   - **Symptom**: Documents fail validation during upgrade
   - **Solution**: Fix document content or update migration logic

3. **Missing Migration Paths**
   - **Symptom**: "No migration path found" errors
   - **Solution**: Implement missing IJsonMigration implementations

### Recovery Procedures

1. **Restore from Snapshots**
   ```bash
   # Find relevant snapshots
   ls *.snapshot.json
   
   # Manually restore if needed
   cp document.json.v1.0.abc123.snapshot.json document.json
   ```

2. **Clear Quarantine**
   ```bash
   # Review quarantined files
   ls quarantine/
   
   # Fix issues and move back to main directory
   mv quarantine/fixed-document.json data/
   ```

## Best Practices

1. **Always plan before executing**
2. **Review plans in staging environment first**
3. **Keep snapshots for rollback capability**
4. **Monitor quarantine directory regularly**
5. **Test migrations thoroughly before deployment**
6. **Document custom migration logic clearly**

## Configuration

### MigrationManifest.json Example

```json
{
  "version": "1.0",
  "discoveryRules": [
    {
      "name": "JsonFiles",
      "parameters": {
        "pattern": "**/*.json",
        "exclude": ["**/quarantine/**", "**/*.snapshot.json"]
      }
    }
  ],
  "targetVersion": "2.0"
}
```

### Environment Variables

- `MIGRATION_MANIFEST_PATH`: Override default manifest location
- `MIGRATION_QUARANTINE_DIR`: Override quarantine directory
- `MIGRATION_LOG_LEVEL`: Control logging verbosity

## Support

For issues or questions:
1. Check this playbook first
2. Review logs in the output
3. Examine quarantine directory
4. Contact the development team with specific error messages and context