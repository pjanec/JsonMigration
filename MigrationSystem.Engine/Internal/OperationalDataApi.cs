using MigrationSystem.Core.Public;
using MigrationSystem.Core.Public.DataContracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MigrationSystem.Engine.Internal;

internal class OperationalDataApi : IOperationalDataApi
{
    private readonly MigrationPlanner _planner;
    private readonly MigrationRunner _runner;

    public OperationalDataApi(MigrationPlanner planner, MigrationRunner runner)
    {
        _planner = planner;
        _runner = runner;
    }

    public Task<MigrationPlan> PlanUpgradeAsync(IEnumerable<DocumentBundle> documentBundles)
    {
        // A real implementation would determine the latest target version from the registry.
        return _planner.PlanUpgradeAsync(documentBundles, "2.0");
    }

    public Task<MigrationPlan> PlanRollbackAsync(IEnumerable<DocumentBundle> documentBundles, string targetVersion)
    {
        return _planner.PlanRollbackAsync(documentBundles, targetVersion);
    }

    public Task<MigrationResult> ExecutePlanAsync(MigrationPlan plan, IEnumerable<DocumentBundle> documentBundles)
    {
        return _runner.ExecutePlanAsync(plan, documentBundles);
    }
}