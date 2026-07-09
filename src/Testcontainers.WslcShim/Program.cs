using Testcontainers.WslcShim.Http;
using Testcontainers.WslcShim.Wslc;

var builder = WebApplication.CreateBuilder(args);
var options = ShimRuntimeOptions.FromConfiguration(builder.Configuration);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(options.FullApiIPAddress, options.FullApiPort);
    serverOptions.Listen(options.RyukBindIPAddress, options.RyukEndpoint.Port);
});

ShimApplication.ConfigureServices(
    builder.Services,
    new WslcCliDockerBackend(new WslcProcessRunner()),
    options,
    new PortListenerClassifier(options));

var app = builder.Build();
app.UseDockerApiVersionPrefix();
ShimApplication.MapRoutes(app);

app.Run();
