using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetPilot.Platform.Windows;
using NetPilot.Security;
using NetPilot.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "NetPilot Service";
});

builder.Services.AddSingleton<OperationPolicy>();
builder.Services.AddSingleton<WindowsNamedPipeServerFactory>();
builder.Services.AddSingleton<WindowsNamedPipePeerAuthenticator>();
builder.Services.AddHostedService<PrivilegedIpcWorker>();

await builder.Build().RunAsync();
