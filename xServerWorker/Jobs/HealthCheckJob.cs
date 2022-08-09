using Quartz;

namespace xServerWorker.Jobs
{
    [DisallowConcurrentExecution]
    public class HealthCheckJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {

            //List of xServers

            // For each each xserver do healthcheck if you are a T3.

            return Task.CompletedTask;


        }
    }
}
