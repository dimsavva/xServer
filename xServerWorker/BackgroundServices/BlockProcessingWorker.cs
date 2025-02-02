﻿using MongoDB.Driver;
using RestSharp;
using System.Text;
using MongoDB.Bson;
using System.Dynamic;
using Common.Models.x42Blockcore;
using Newtonsoft.Json;
using Common.Enums;
using Common.Models.XDocuments.DNS;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json.Serialization;
using MongoDB.Bson.Serialization;
using xServerWorker.Services;
using MongoDB.Driver.Builders;
using Common.Models.XDocuments.Zones;
using Common.Models.XServer;
using Common.Services;
using System.Linq.Expressions;

namespace xServerWorker.BackgroundServices
{
    public class BlockProcessingWorker : BackgroundService
    {
        private readonly ILogger<BlockProcessingWorker> _logger;
        private static int latestBlockProcessed = 1910000;

#if DEBUG
        private readonly RestClient _restClient = new RestClient("http://localhost:42220/api/");
#else
        private readonly RestClient _restClient = new RestClient("http://x42core:42220/api/");
#endif




        private readonly MongoClient _client;
        private readonly IMongoDatabase _db;
        private readonly PowerDnsRestClient _powerDnsRestClient;

        private static string _powerDnsHost = "";
        private static string _powerDnsApiKey = "";
        private static string _xServerHost = "http://x42server:4242/";
        private static string _feeAddress = "";


        private static List<SimpleBlockModel> _blockHashes = new List<SimpleBlockModel>();

        public BlockProcessingWorker(ILogger<BlockProcessingWorker> logger, PowerDnsRestClient powerDnsRestClient)
        {
            var mongoConnectionString = Environment.GetEnvironmentVariable("MONGOCONNECTIONSTRING");

            _logger = logger;

            _powerDnsHost = Environment.GetEnvironmentVariable("POWERDNSHOST") ?? "";
            _powerDnsApiKey = Environment.GetEnvironmentVariable("PDNS_API_KEY") ?? "";



#if DEBUG
            _powerDnsHost = "https://poweradmin.xserver.network";
            _powerDnsApiKey = "cmp4V1Z0MnprRVRMbE10";
            _xServerHost = "http://127.0.0.1:4242/";
            _client = new MongoClient($"mongodb://localhost:27017");


#else
            _client = new MongoClient(mongoConnectionString);
            //_xServerHost = Environment.GetEnvironmentVariable("XSERVER_BACKEND") ?? "";

#endif



            _db = _client.GetDatabase("xServerDb");

            var options = new CreateIndexOptions() { Unique = false };
            var field = new StringFieldDefinition<BsonDocument>("keyAddress");
            var indexDefinition = new IndexKeysDefinitionBuilder<BsonDocument>().Ascending(field);

            var indexModel = new CreateIndexModel<BsonDocument>(indexDefinition, options);
            _db.GetCollection<BsonDocument>("DnsZones").Indexes.CreateOne(indexModel);
            _powerDnsRestClient = powerDnsRestClient;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                var blockCount = await GetBlockCount();
                _feeAddress = await GetMyFeeAddress();

                var dictionaryCollection = _db.GetCollection<BsonDocument>("Dictionary");


                var filter = Builders<BsonDocument>.Filter.Eq("_id", "latestBlock");
                var document = dictionaryCollection.Find(filter).FirstOrDefault();

                if (document != null)
                {
                    var id = document["value"].ToString();

                    if (id != null)
                    {
                        latestBlockProcessed = int.Parse(id);

                    }

                }
                else
                {
                    dynamic lastBlockEntry = new ExpandoObject();
                    lastBlockEntry._id = "latestBlock";
                    lastBlockEntry.value = latestBlockProcessed;


                    var jsonstring = JsonConvert.SerializeObject(lastBlockEntry);
                    var updateDocument = BsonSerializer.Deserialize<BsonDocument>(jsonstring);

                    dictionaryCollection.InsertOne(updateDocument);
                    document = updateDocument;

                }


                _logger.LogInformation($"Latest block processed : {latestBlockProcessed}");
                _logger.LogInformation($"Latest block height : {blockCount}");

                var taskList = new List<Task<string>>();
                var consinueTaskList = new List<Task>();


                _logger.LogInformation("Processsing Blocks...");




                while (!stoppingToken.IsCancellationRequested)
                {
                    if (latestBlockProcessed < blockCount && _feeAddress != "")
                    {
                        await ProcessBlock(stoppingToken);

                        if (document != null)
                        {
                            var builder = Builders<BsonDocument>.Filter;

                            filter = builder.Eq("_id", "latestBlock");
                            var update = Builders<BsonDocument>.Update.Set("value", latestBlockProcessed);
                            dictionaryCollection.UpdateOne(filter, update);

                        }

                    }
                    else
                    {


                        blockCount = await GetBlockCount();

                    }
                }
            }
            catch (Exception e)
            {

                throw;
            }
        }

        private async Task ProcessBlock(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Processing Block : {latestBlockProcessed}");

            var blockHash = await GetBlockHash(latestBlockProcessed);

            var block = await GetBlock(blockHash);

            var paidToMyFeeAddress = false;
            double amount = 0;
            foreach (var transaction in block.Transactions)
            {
                foreach (var vout in transaction.Vout)
                {

                    if (vout.ScriptPubKey.Addresses != null && vout.ScriptPubKey.Addresses.Contains(_feeAddress))
                    {

                        paidToMyFeeAddress = true;
                        amount = vout.Value;

                    }
                }
            }

            paidToMyFeeAddress =  block.Transactions.Where(l => l.Vout.Any(x => x.ScriptPubKey.Addresses != null && x.ScriptPubKey.Addresses.Contains(_feeAddress))).Any();

            if (paidToMyFeeAddress)
            {
                var vouts = block.Transactions.SelectMany(l => l.Vout).Where(l => l.ScriptPubKey.Addresses != null && l.ScriptPubKey.Addresses.Contains(_feeAddress)).ToList();

                amount = vouts.Sum(l => l.Value);


            }

            var vOutList = block.Transactions.SelectMany(l => l.Vout);

 

            foreach (var vOut in vOutList)
            {

                if (vOut.ScriptPubKey.Asm.Contains("OP_RETURN"))
                {
                    var asmHex = vOut.ScriptPubKey.Asm.Replace("OP_RETURN", "").Replace(" ", "");
                    if (asmHex.Length != 66)
                    {
                        _logger.LogInformation($"Instruction found with hex {asmHex} at block {latestBlockProcessed}");
                        byte[] data = FromHex(asmHex);

                        string instructionHash = Encoding.ASCII.GetString(data);
                        _logger.LogInformation($"Instruction hash {instructionHash}");

                        _logger.LogInformation("------------------------------------------------------------------------");
                        _logger.LogInformation($"Check Mongo Pending Transactions, if it exists, process the instruction");
                        _logger.LogInformation("------------------------------------------------------------------------");

                        var xDocumentHashCollection = _db.GetCollection<BsonDocument>("XDocumentHashReference");

                        

                        var filter = Builders<BsonDocument>.Filter.Eq("hash", instructionHash);
                        var document = xDocumentHashCollection.Find(filter).FirstOrDefault();

                        var xDocumentPendingCollection = _db.GetCollection<BsonDocument>("xDocumentPending");

                        if (document != null)
                        {
                            var id = document["_id"].ToString();


                            filter = Builders<BsonDocument>.Filter.Eq("_id", id);
                            document = xDocumentPendingCollection.Find(filter).FirstOrDefault();

                            try
                            {
                                

                                    await ProcessInstruction(document, xDocumentPendingCollection, amount,paidToMyFeeAddress, block.Height);

                                 

                            }
                            catch (Exception e)
                            {

                                _logger.LogError(e.Message);
                            }


                        }

                    }
                }
            }
            latestBlockProcessed++;
        }

        private async Task ProcessInstruction(BsonDocument document, IMongoCollection<BsonDocument> xDocumentPendingCollection, double amountPaid, bool paidToMyFeeAddress, int blockHeight)
        {
            if (document != null)
            {
                int instructionType = Convert.ToInt32(document["instructionType"]);
                var dynamicObject = JsonConvert.DeserializeObject<dynamic>(document.ToString());
                string _keyAddress = dynamicObject["keyAddress"];

                var data = dynamicObject["data"];
 

                switch (instructionType)
                {
                    case (int)InstructionTypeEnum.NewDnsZone:

                        await NewDnsZone(document, xDocumentPendingCollection, dynamicObject, blockHeight, amountPaid, paidToMyFeeAddress);

                        break;
                    case (int)InstructionTypeEnum.UpdateDnsZone:

                        await UpdateDnsZone(document, xDocumentPendingCollection, dynamicObject, blockHeight);

                        break;

                    case (int)InstructionTypeEnum.RegisterNewNameserver:

                        if (dynamicObject != null)
                        {

                            string nameServer = (string)dynamicObject["data"]["ns"];
                            string ipAddress = (string)dynamicObject["data"]["ip"];

                        }

                        break;


                    default:

                        throw new Exception($"Instruction Type ({instructionType}) not found");

                }

            }
        }

        public async Task UpdateDnsZone(BsonDocument document, IMongoCollection<BsonDocument> xDocumentPendingCollection, dynamic dynamicObject, int blockHeight)
        {
            if (dynamicObject != null)
            {


                _logger.LogInformation($"Update Zone Instruction Found");
                _logger.LogInformation($"Updating DNS Zone");

                string zone = dynamicObject["data"]["zone"];
                var rrSetsData = dynamicObject["data"]["rrsets"];

                var rrSets = JsonConvert.DeserializeObject<List<RrSet>>(rrSetsData.ToString());

                var zoneUpdate = false;

                try
                {
                    await _powerDnsRestClient.UpdateDNSZone(dynamicObject, blockHeight, rrSets);
                    zoneUpdate = true;

                }
                catch (Exception e)
                {
                    _logger.LogInformation($"Updating DNS zone '{zone}'failed");
                    _logger.LogInformation(e.Message);

                }

                if (zoneUpdate)
                {

                    MoveDocument(document, xDocumentPendingCollection);

                }

            }
        }

        private async Task NewDnsZone(BsonDocument document, IMongoCollection<BsonDocument> xDocumentPendingCollection, dynamic dynamicObject, int blockHeight, double amountPaid, bool paidToMyFeeAddress)
        {
            if (dynamicObject != null)
            {
                string zone = (string)dynamicObject["data"]["zone"];

                _logger.LogInformation($"New Zone Instruction Found");
                _logger.LogInformation($"Creating DNS Zone");

                var zoneCreated = false;

                try
                {
 
                    await _powerDnsRestClient.CreateDNSZone(dynamicObject, blockHeight);
                    zoneCreated = true;

                }
                catch (Exception e)
                {
                    _logger.LogInformation($"Creating DNS zone '{zone}'failed");
                    _logger.LogInformation(e.Message);

                }

                if (zoneCreated)
                {

                    MoveDocument(document, xDocumentPendingCollection);

                }

            }
        }

        private void MoveDocument(BsonDocument document, IMongoCollection<BsonDocument> xDocumentPendingCollection)
        {
            xDocumentPendingCollection.DeleteOne(document);

            

            var xDocumentCollection = _db.GetCollection<BsonDocument>("xDocument");
            xDocumentCollection.InsertOne(document);
        }




        private async Task<string> GetMyFeeAddress()
        {



            var client = new RestClient(_xServerHost);
            var request = new RestRequest($"status", Method.Get);
            request.AddHeader("content-type", "application/json");


            var response = await client.ExecuteAsync<StatusResult>(request);

            return response.Data.FeeAddress;
        }




        private static void ValidateModel(object app)
        {
            var context = new ValidationContext(app, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(app, context, validationResults, true);

            if (!isValid)
            {

                throw new Exception(validationResults.FirstOrDefault().ErrorMessage);

            }
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

        private decimal GetFee(InstructionTypeEnum instructionTypeEnum)
        {

            switch (instructionTypeEnum)
            {
                case InstructionTypeEnum.NewDnsZone:
                    return 2;

                case InstructionTypeEnum.UpdateDnsZone:
                    return 0.5m;

                default:
                    return 0;

            }


        }
    }
}
