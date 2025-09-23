using System.Collections.Generic;

namespace MigrationSystem.Tests.TestMigrations.PkgConf;

public class PkgConfV1_0
{
    public int Timeout { get; set; }
    public List<string> Plugins { get; set; } = new();
}