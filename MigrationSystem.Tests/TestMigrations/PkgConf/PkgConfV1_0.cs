using MigrationSystem.Core.Public;
using System.Collections.Generic;

namespace MigrationSystem.Tests.TestMigrations.PkgConf;

[SchemaVersion("1.0", "PkgConf")]
public class PackageConfiguration // Renamed from PkgConfV1_0
{
    public int Timeout { get; set; }
    public List<string> Plugins { get; set; } = new();
}