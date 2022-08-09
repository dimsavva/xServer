using MongoDB.Bson;
using MongoDB.Driver;
using RestSharp;
using System.Text;

namespace xServerWorker.BackgroundServices
{
    public class BlockProcessingWorker : BackgroundService
    {
        private readonly ILogger<BlockProcessingWorker> _logger;

        private static int latestBlockProcessed = 1876379;
        private readonly RestClient _restClient = new RestClient("https://x42.cybits.org/api/");
        private readonly MongoClient _client;
        private readonly IMongoDatabase _db;


        public BlockProcessingWorker(ILogger<BlockProcessingWorker> logger)
        {
            _logger = logger;
            _client = new MongoClient("mongodb://localhost:27017/");
            _db = _client.GetDatabase("xServerDb");

        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var blockCount = await GetBlockCount();

            Console.Clear();

            Console.WriteLine($"Latest block processed : {latestBlockProcessed}");
            Console.WriteLine($"Latest block height : {blockCount}");
            await Task.Delay(5000, stoppingToken);

            Console.WriteLine();
            Console.WriteLine("Processsing Blocks...");
            Console.WriteLine();

            await Task.Delay(2000, stoppingToken);



            while (!stoppingToken.IsCancellationRequested)
            {
                if (latestBlockProcessed < blockCount)
                {
                    await ProcessBlock(stoppingToken);
                }
                else
                {

                    await Task.Delay(5000, stoppingToken);

                    blockCount = await GetBlockCount();

                }
            }
        }

        private async Task ProcessBlock(CancellationToken stoppingToken)
        {
            Console.WriteLine($"Processing Block : {latestBlockProcessed}");

            var blockHash = await GetBlockHash(latestBlockProcessed);

            var block = await GetBlock(blockHash);

            var vOutList = block.Transactions.SelectMany(l => l.Vout);

            foreach (var vOut in vOutList)
            {

                if (vOut.ScriptPubKey.Asm.Contains("OP_RETURN"))
                {
                    var asmHex = vOut.ScriptPubKey.Asm.Replace("OP_RETURN", "").Replace(" ", "");
                    if (asmHex.Length != 66)
                    {
                        Console.WriteLine($"Instruction found with hex {asmHex} at block {latestBlockProcessed}");
                        byte[] data = FromHex(asmHex);

                        string instructionHash = Encoding.ASCII.GetString(data);
                        Console.WriteLine($"Instruction hash {instructionHash}");

                        Console.WriteLine("------------------------------------------------------------------------");
                        Console.WriteLine($"Check Mongo Pending Transactions, if it exists, process the instruction");
                        Console.WriteLine("------------------------------------------------------------------------");

                        var xDocumentHashCollection = _db.GetCollection<BsonDocument>("XDocumentHashReference");



                        var filter = Builders<BsonDocument>.Filter.Eq("hash", instructionHash);
                        var document = xDocumentHashCollection.Find(filter).FirstOrDefault();

                        var xDocumentPendingCollection = _db.GetCollection<BsonDocument>("xDocumentPending");

                        var id = document["_id"].ToString();


                        filter = Builders<BsonDocument>.Filter.Eq("_id", id);
                        document = xDocumentPendingCollection.Find(filter).FirstOrDefault();

                        xDocumentPendingCollection.DeleteOne(document);

                        var xDocumentCollection = _db.GetCollection<BsonDocument>("xDocument");
                        xDocumentCollection.InsertOne(document);

                        Console.WriteLine(document.ToString());

                        await Task.Delay(5000, stoppingToken);


                    }
                }
            }
            latestBlockProcessed++;
        }


        private async Task<int> GetBlockCount()
        {
            var request = new RestRequest($"BlockStore/getblockcount");
            var result = await _restClient.GetAsync<int>(request);

            return result;
        }

        private async Task<string> GetBlockHash(int height)
        {


            var request = new RestRequest($"Consensus/getblockhash?height={height}");
            var result = await _restClient.GetAsync<string>(request);

            if (result != null) { return result; }

            var errorMessage = $"Block at height {height} not found";
            _logger.LogError(errorMessage);

            return errorMessage;


        }

        private async Task<BlockModel> GetBlock(string blockHash)
        {

            var request = new RestRequest($"BlockStore/block?Hash={blockHash}&ShowTransactionDetails=true&OutputJson=true");

            var result = await _restClient.GetAsync<BlockModel>(request);
            if (result != null) { return result; }

            var errorMessage = $"Block hash {blockHash} not found";
            _logger.LogError(errorMessage);

            return null;
        }



        public static byte[] FromHex(string hex)
        {
            hex = hex.Replace("-", "");
            byte[] raw = new byte[hex.Length / 2];
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return raw;
        }
    }
}
