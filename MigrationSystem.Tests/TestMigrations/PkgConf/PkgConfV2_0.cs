using System.Collections.Generic;

namespace MigrationSystem.Tests.TestMigrations.PkgConf;

public class PkgConfV2_0
{
    public int Execution_timeout { get; set; }
    public Dictionary<string, PkgConfV2_0_Plugin> Plugins { get; set; } = new();
    public PkgConfV2_0_Reporting Reporting { get; set; } = new();
}

public class PkgConfV2_0_Plugin
{
    public bool Enabled { get; set; }
}

public class PkgConfV2_0_Reporting
{
    public string Format { get; set; } = "";
}