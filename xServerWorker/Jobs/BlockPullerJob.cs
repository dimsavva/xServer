using Quartz;
using RestSharp;

namespace xServerWorker.Jobs
{
    [DisallowConcurrentExecution]
    public class BlockPullerJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            using (var loggerFactory = LoggerFactory.Create
            (
                builder =>
                {
                    builder.AddConsole();
                }
            ))
            {

                var _logger = loggerFactory.CreateLogger<BlockPullerJob>();

                var client = new RestClient("https://x42.cybits.org/api/");

                var request = new RestRequest("BlockStore/getblockcount");
                var response = await client.GetAsync<int>(request);
              
            }
        }
    }
}
