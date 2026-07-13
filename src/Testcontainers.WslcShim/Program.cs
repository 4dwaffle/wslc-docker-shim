using Testcontainers.WslcShim;
using Testcontainers.WslcShim.Cli;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Http;
using Testcontainers.WslcShim.Http.Models;
using Testcontainers.WslcShim.Watch;
using Testcontainers.WslcShim.Wslc;

if (CliHelp.TryWrite(args, Console.Out))
{
    return;
}

var launchOptions = ShimLaunchOptions.Parse(args);
var launchError = launchOptions.GetValidationError(SystemWatchTerminal.IsInteractive);
if (launchError is not null)
{
    Console.Error.WriteLine($"error: {launchError}");
    Console.Error.WriteLine("Use --help to see available options.");
    Environment.ExitCode = 2;
    return;
}

var builder = WebApplication.CreateBuilder(launchOptions.ApplicationArguments);
var options = ShimRuntimeOptions.FromConfiguration(builder.Configuration);

var processRunner = (IWslcProcessRunner)new WslcProcessRunner();

IDockerBackend backend = new WslcCliDockerBackend(processRunner);
if (launchOptions.WatchEnabled)
{
    var watchDashboard = new WatchDashboardState();
    var terminal = new SystemWatchTerminal();
    backend = new WatchingDockerBackend(backend, watchDashboard);
    builder.Services.AddSingleton(watchDashboard);
    builder.Services.AddSingleton<IWatchTerminal>(terminal);
    builder.Services.AddSingleton<WatchDashboardRenderer>();
    builder.Services.AddSingleton(processRunner);
    builder.Services.AddHostedService<WatchDashboardService>();
    builder.Services.AddHostedService<WslcContainerInventoryService>();
    builder.Logging.ClearProviders();
}

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(options.FullApiIPAddress, options.FullApiPort);
    serverOptions.Listen(options.RyukBindIPAddress, options.RyukEndpoint.Port);
});

ShimApplication.ConfigureServices(
    builder.Services,
    backend,
    options,
    new PortListenerClassifier(options));

var app = builder.Build();

app.UseDockerApiVersionPrefix();
ShimApplication.MapRoutes(app);

app.Run();
