using xServerWorker;
using xServerWorker.BackgroundServices;
using xServerWorker.Services;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<BlockProcessingWorker>();
        try
        {
            services.AddSingleton<BroadCaster, BroadCaster>();

        }
        catch (Exception)
        {

            throw;
        }

    })
    .Build();

await QuartzJobConfigurator.ConfigureJobsAsync();

await host.RunAsync();