using MigrationSystem.Core.Public;
using MigrationSystem.Engine.Internal;
using MigrationSystem.Engine.Discovery;

namespace MigrationSystem.Engine.Public;

// This internal class implements the main public interface.
// It will be composed by the builder and will hold references
// to all the internal services.
internal class MigrationSystemFacade : IMigrationSystem
{
    public IApplicationApi Application { get; }
    public IDataApi Data { get; } // To be implemented
    public IOperationalApi Operations { get; }
    public IOperationalDataApi OperationalData { get; }
    public IDiscoveryService Discovery { get; }

    // The constructor will receive all the configured internal services from the DI container.
    public MigrationSystemFacade(MigrationRegistry registry, SnapshotManager snapshotManager, SchemaRegistry schemaRegistry)
    {
        // Create internal components
        var planner = new MigrationPlanner(registry);
        var merger = new ThreeWayMerger(registry);
        var runner = new MigrationRunner(merger, registry);

        // Compose the concrete implementations of our public facades
        this.Application = new ApplicationApi(registry, schemaRegistry, snapshotManager);
        this.OperationalData = new OperationalDataApi(planner, runner);
        this.Discovery = new FileDiscoverer();
        this.Operations = new OperationalApi(this.Discovery, this.OperationalData, snapshotManager);
        
        // ... instantiate other facades here
        this.Data = null!; // Placeholder
    }
}