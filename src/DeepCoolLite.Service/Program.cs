using DeepCoolLite.Core;
using DeepCoolLite.Service;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory, // services start in System32
});

// No config file - the service ships as a single exe. Options (e.g. --LogReadings=true) come from the command line.
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

builder.Services.AddWindowsService(o => o.ServiceName = AppInfo.ServiceName);
builder.Services.AddSingleton<ServiceState>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<PipeServer>();

builder.Build().Run();
