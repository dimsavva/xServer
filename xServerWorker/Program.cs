using xServerWorker;
using xServerWorker.BackgroundServices;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<BlockProcessingWorker>();

    })
    .Build();

await QuartzJobConfigurator.ConfigureJobsAsync();

await host.RunAsync();
