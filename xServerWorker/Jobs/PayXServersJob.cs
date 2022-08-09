using Quartz;

namespace xServerWorker.Jobs
{
    [DisallowConcurrentExecution]
    public class PayXServersJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {

            // Pay xServer for hosting

            throw new NotImplementedException();
        }
    }
}
