using Microsoft.Extensions.DependencyInjection;
using MigrationSystem.Core.Public;
using MigrationSystem.Engine.Public;
using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MigrationSystem.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Migration System CLI");
            Console.WriteLine("Usage: MigrationSystem.Cli <command> [options]");
            Console.WriteLine("Commands:");
            Console.WriteLine("  plan-upgrade     Generate a migration plan");
            Console.WriteLine("  migrate          Execute a migration plan");
            Console.WriteLine("  help             Show this help message");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddMigrationSystem(options =>
        {
            options.WithMigrationsFromAssembly(typeof(Program).Assembly);
            options.WithDefaultManifestPath("./MigrationManifest.json");
            options.WithQuarantineDirectory("./quarantine");
        });
        var serviceProvider = services.BuildServiceProvider();
        var migrationSystem = serviceProvider.GetRequiredService<IMigrationSystem>();

        var command = args[0];
        try
        {
            switch (command.ToLower())
            {
                case "plan-upgrade":
                    await HandlePlanUpgrade(args, migrationSystem.Operations);
                    break;
                
                case "migrate":
                    await HandleMigrate(args, migrationSystem.Operations);
                    break;
                
                case "help":
                    ShowHelp();
                    break;
                
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    ShowHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        
        return 0;
    }

    private static async Task HandlePlanUpgrade(string[] args, IOperationalApi opsApi)
    {
        Console.WriteLine("Planning migration upgrade...");
        
        // Find the optional --manifest argument
        var manifestPathArg = args.FirstOrDefault(a => a.StartsWith("--manifest="))?.Split('=')[1];
        
        // Pass the argument to the API call
        var plan = await opsApi.PlanUpgradeFromManifestAsync(manifestPathArg);
        var planFileName = $"plan-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        var planJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(planFileName, planJson);
        
        Console.WriteLine($"--- MIGRATION PLAN SUMMARY ---");
        Console.WriteLine($"Target Version: {plan.Header.TargetVersion}");
        Console.WriteLine($"Generated At: {plan.Header.GeneratedAtUtc}");
        Console.WriteLine($"Total Actions: {plan.Actions.Count}");
        
        // Show action breakdown
        var actionGroups = plan.Actions.GroupBy(a => a.ActionType);
        foreach (var group in actionGroups)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }
        
        Console.WriteLine($"Plan saved to: {planFileName}");
        
        if (manifestPathArg != null)
        {
            Console.WriteLine($"Used manifest: {manifestPathArg}");
        }
        else
        {
            Console.WriteLine("Used default manifest path");
        }
    }

    private static async Task HandleMigrate(string[] args, IOperationalApi opsApi)
    {
        var planPath = args.FirstOrDefault(a => a.StartsWith("--plan="))?.Split('=')[1];
        if (string.IsNullOrEmpty(planPath))
        {
            Console.WriteLine("Error: The 'migrate' command requires a --plan=<path> argument.");
            Console.WriteLine("Example: migrate --plan=plan-20250922192800.json");
            return;
        }

        if (!File.Exists(planPath))
        {
            Console.WriteLine($"Error: Plan file not found: {planPath}");
            return;
        }

        Console.WriteLine($"Executing migration plan: {planPath}");
        
        var planJson = await File.ReadAllTextAsync(planPath);
        var planToExecute = JsonSerializer.Deserialize<MigrationSystem.Core.Public.DataContracts.MigrationPlan>(planJson);
        
        if (planToExecute == null)
        {
            Console.WriteLine("Error: Failed to parse migration plan.");
            return;
        }

        var result = await opsApi.ExecutePlanAgainstFileSystemAsync(planToExecute);
        
        Console.WriteLine($"--- MIGRATION RESULT ---");
        Console.WriteLine($"Status: {result.Summary.Status}");
        Console.WriteLine($"Duration: {result.Summary.Duration.TotalSeconds:F2} seconds");
        Console.WriteLine($"Processed: {result.Summary.Processed}");
        Console.WriteLine($"Succeeded: {result.Summary.Succeeded}");
        Console.WriteLine($"Failed: {result.Summary.Failed}");
        Console.WriteLine($"Skipped: {result.Summary.Skipped}");

        // Save result to file
        var resultFileName = $"result-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(resultFileName, resultJson);
        Console.WriteLine($"Detailed result saved to: {resultFileName}");

        if (result.Summary.Failed > 0)
        {
            Console.WriteLine($"Warning: {result.Summary.Failed} documents failed migration and may be in quarantine.");
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Migration System CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: MigrationSystem.Cli <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  plan-upgrade [--manifest=<path>]    Generate a dry-run migration plan");
        Console.WriteLine("  migrate --plan=<file>               Execute a specific migration plan");
        Console.WriteLine("  help                                Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  MigrationSystem.Cli plan-upgrade");
        Console.WriteLine("  MigrationSystem.Cli plan-upgrade --manifest=./custom-manifest.json");
        Console.WriteLine("  MigrationSystem.Cli migrate --plan=plan-20250922192800.json");
    }
}
