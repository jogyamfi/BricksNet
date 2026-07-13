using BricksNet.Samples.Demos;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)   // resolve relative to the exe output dir
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.User.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

const string Usage = """
    BricksNet Samples
    =================
    Usage:  dotnet run -- <demo>

    Available demos:
      FluentPat    — Fluent API with Personal Access Token (PAT)
      FluentOAuth  — Fluent API with OAuth 2.0 PKCE (launches a browser window)
      AdoNetDapper — ADO.NET provider with Dapper (dynamic + typed POCO)
      Di           — Dependency Injection via AddBricksNet()

    Credentials:
      Create appsettings.User.json (git-ignored) beside appsettings.json,
      or set environment variables using double-underscore as separator:
        Databricks__Host=adb-xxx.azuredatabricks.net
        Databricks__HttpPath=/sql/1.0/warehouses/yyy
        Databricks__Token=dapi...
    """;

var demo = args.Length > 0 ? args[0] : string.Empty;

if (string.IsNullOrEmpty(demo))
{
    Console.WriteLine(Usage);
    //return;
}

Console.WriteLine($"Running demo: {demo}");
Console.WriteLine();

try
{
    await (demo switch
    {
        "FluentPat"    => FluentPatDemo.RunAsync(config),
        "FluentOAuth"  => FluentOAuthDemo.RunAsync(config),
        "AdoNetDapper" => AdoNetDapperDemo.RunAsync(config),
        "Di"           => DiDemo.RunAsync(config),
        _              => FluentPatDemo.RunAsync(config),
       //  _              => Task.FromException(new ArgumentException($"Unknown demo '{demo}'."))
    });
}
catch (InvalidOperationException ex) when (ex.Message.EndsWith("not configured.", StringComparison.Ordinal))
{
    await Console.Error.WriteLineAsync($"Configuration error: {ex.Message}");
    await Console.Error.WriteLineAsync("Populate appsettings.User.json or set environment variables (e.g. Databricks__Token=...).");
    Environment.Exit(1);
}
catch (UriFormatException)
{
    await Console.Error.WriteLineAsync("Configuration error: 'Host' or an OAuth endpoint is not a valid hostname.");
    await Console.Error.WriteLineAsync("Populate appsettings.User.json or set environment variables (e.g. Databricks__Host=...).");
    Environment.Exit(1);
}
catch (ArgumentException ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    Console.WriteLine();
    Console.WriteLine(Usage);
    Environment.Exit(1);
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Demo failed ({ex.GetType().Name}): {ex.Message}");
    await Console.Error.WriteLineAsync("Verify your Host, HttpPath, and credentials in appsettings.User.json or environment variables.");
    Environment.Exit(1);
}
