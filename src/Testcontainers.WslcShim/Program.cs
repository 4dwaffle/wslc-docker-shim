using Testcontainers.WslcShim;
using Testcontainers.WslcShim.Cli;
using Testcontainers.WslcShim.Http;
using Testcontainers.WslcShim.Http.Models;
using Testcontainers.WslcShim.Watch;
using Testcontainers.WslcShim.Wslc;

if (CliHelp.TryWrite(args, Console.Out))
{
    return;
}

var launchOptions = ShimLaunchOptions.Parse(args);
var builder = WebApplication.CreateBuilder(launchOptions.ApplicationArguments);
var options = ShimRuntimeOptions.FromConfiguration(builder.Configuration);

var processRunner = (IWslcProcessRunner)new WslcProcessRunner();
ConsoleWatchActivityReporter? watchReporter = null;
if (launchOptions.WatchEnabled)
{
    watchReporter = ConsoleWatchActivityReporter.CreateDefault();
    var watchRequestContext = new WatchRequestContext();
    processRunner = new WatchingWslcProcessRunner(processRunner, watchReporter, watchRequestContext);
    builder.Services.AddSingleton<IWatchActivityReporter>(watchReporter);
    builder.Services.AddSingleton(watchRequestContext);
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
}

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(options.FullApiIPAddress, options.FullApiPort);
    serverOptions.Listen(options.RyukBindIPAddress, options.RyukEndpoint.Port);
});

ShimApplication.ConfigureServices(
    builder.Services,
    new WslcCliDockerBackend(processRunner),
    options,
    new PortListenerClassifier(options));

var app = builder.Build();
if (watchReporter is not null)
{
    app.UseMiddleware<WatchRequestMiddleware>();
    app.Lifetime.ApplicationStarted.Register(() => watchReporter.WriteStartup(options));
    app.Lifetime.ApplicationStopping.Register(watchReporter.WriteStopping);
}

app.UseDockerApiVersionPrefix();
ShimApplication.MapRoutes(app);

app.Run();
