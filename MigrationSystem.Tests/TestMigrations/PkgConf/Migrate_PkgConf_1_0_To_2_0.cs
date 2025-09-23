using MigrationSystem.Core.Contracts;
using System.Linq;
using System.Threading.Tasks;

namespace MigrationSystem.Tests.TestMigrations.PkgConf;

public class Migrate_PkgConf_1_0_To_2_0 : IJsonMigration<PkgConfV1_0, PkgConfV2_0>
{
    public Task<PkgConfV2_0> ApplyAsync(PkgConfV1_0 fromDto)
    {
        var toDto = new PkgConfV2_0
        {
            Execution_timeout = fromDto.Timeout,
            Plugins = fromDto.Plugins.ToDictionary(p => p, _ => new PkgConfV2_0_Plugin { Enabled = true }),
            Reporting = new PkgConfV2_0_Reporting { Format = "json" }
        };
        return Task.FromResult(toDto);
    }

    public Task<PkgConfV1_0> ReverseAsync(PkgConfV2_0 toDto)
    {
        var fromDto = new PkgConfV1_0
        {
            Timeout = toDto.Execution_timeout,
            Plugins = toDto.Plugins.Keys.ToList()
        };
        return Task.FromResult(fromDto);
    }
}