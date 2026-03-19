using ArchonAI.Migrations;

if (args.Length > 0 && args[0] == "--migrate")
{
    var connectionString = args.Length > 1
        ? args[1]
        : Environment.GetEnvironmentVariable("ArchonAIPersistence__ConnectionString");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine(
            "Error: Connection string required. " +
            "Pass as second argument or set ArchonAIPersistence__ConnectionString.");
        return 1;
    }

    Console.WriteLine("Running database migrations...");
    var runner = new MigrationRunner(connectionString);
    var result = runner.Run();

    if (result.Success)
    {
        Console.WriteLine($"Migrations completed in {result.ElapsedMs}ms. {result.ScriptsExecuted.Count} script(s) executed.");
        foreach (var script in result.ScriptsExecuted)
        {
            Console.WriteLine($"  Applied: {script}");
        }
        return 0;
    }

    Console.Error.WriteLine($"Migration FAILED after {result.ElapsedMs}ms.");
    Console.Error.WriteLine($"  Failed script: {result.FailedScript}");
    Console.Error.WriteLine($"  Error: {result.Error}");
    return 1;
}

Console.WriteLine("ArchonAI CLI");
Console.WriteLine("Usage:");
Console.WriteLine("  archonai --migrate [connection-string]  Run database migrations");
return 0;
