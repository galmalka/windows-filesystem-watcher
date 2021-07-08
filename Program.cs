using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FileAccessTracker;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "File Access Tracker Service";
    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<FileAccessTrackerService>();
    })
    .Build();

await host.RunAsync();