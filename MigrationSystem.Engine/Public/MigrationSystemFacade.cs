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
    public IDataApi Data { get; }
    public IOperationalApi Operations { get; }
    public IOperationalDataApi OperationalData { get; }
    public IDiscoveryService Discovery { get; }

    // The constructor will receive all the configured internal services from the DI container.
    public MigrationSystemFacade(MigrationRegistry registry, SnapshotManager snapshotManager, SchemaRegistry schemaRegistry, QuarantineManager quarantineManager, IDiscoveryService discoveryService)
    {
        // Store the discovery service instance
        this.Discovery = discoveryService;
        
        // Create internal components
        var planner = new MigrationPlanner(registry);
        var merger = new ThreeWayMerger(registry);
        var runner = new MigrationRunner(merger, registry);
        
        this.OperationalData = new OperationalDataApi(planner, runner);
        this.Data = new DataApi(registry, schemaRegistry, this.OperationalData);
        this.Application = new ApplicationApi(registry, schemaRegistry, snapshotManager, quarantineManager);
        this.Operations = new OperationalApi(discoveryService, this.OperationalData, snapshotManager, quarantineManager);
    }
}