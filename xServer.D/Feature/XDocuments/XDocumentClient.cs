﻿using Common.Models.Graviex;
using Common.Models.OrderBook;
using Common.Utils;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using x42.Feature.PowerDns;
using x42.Feature.X42Client;

namespace x42.Feature.XDocuments
{
    public class XDocumentClient
    {
        private readonly MongoClient _client;
        private readonly IMongoDatabase _db;
        private readonly PowerDnsFeature _powerDnsService;
        private readonly X42Node _x42Client;


        public XDocumentClient(PowerDnsFeature powerDnsService, X42Node x42Client)
        {
            var conventionPack = new ConventionPack { new CamelCaseElementNameConvention() };
            ConventionRegistry.Register("camelCase", conventionPack, t => true);


            var mongoUser = Environment.GetEnvironmentVariable("MONGO_USER");
            var mongoPassword = Environment.GetEnvironmentVariable("MONGO_PASSWORD");

#if DEBUG
            _client = new MongoClient($"mongodb://localhost:27017");
#else
            _client = new MongoClient($"mongodb://{mongoUser}:{mongoPassword}@xDocumentStore:27017/");
#endif



            _db = _client.GetDatabase("xServerDb");
            _powerDnsService = powerDnsService;
            _x42Client = x42Client;
        }

        public async Task<object> GetDocumentById(Guid Id)
        {


            // Get xDocument Collection Reference
            var xDocumentCollection = _db.GetCollection<BsonDocument>("xDocument");

            var filter = Builders<BsonDocument>.Filter.Eq("_id", Id.ToString());
            var document = xDocumentCollection.Find(filter).FirstOrDefault();

            var dynamicObject = JsonConvert.DeserializeObject<dynamic>(document.ToString());


            // Convert request to JSON string

            return dynamicObject;

        }

        public async Task<object> GetDocumentByHash(string hash)
        {


            // Get xDocument Collection Reference
            var xDocumentHashCollection = _db.GetCollection<BsonDocument>("XDocumentHashReference");

            var x = xDocumentHashCollection.GetHashCode();

            var filter = Builders<BsonDocument>.Filter.Eq("hash", hash);
            var document = xDocumentHashCollection.Find(filter).FirstOrDefault();

            var xDocumentCollection = _db.GetCollection<BsonDocument>("xDocument");

            var id = document["_id"].ToString();


            filter = Builders<BsonDocument>.Filter.Eq("_id", id);
            document = xDocumentCollection.Find(filter).FirstOrDefault();

            var s = document.ToString();



            var dynamicObject = JsonConvert.DeserializeObject<dynamic>(document.ToString());

            string serialized = Serialize(dynamicObject);

            var matched = HashString(serialized) == hash;


            // Convert request to JSON string

            return dynamicObject;

        }

        public Task<decimal> GetPriceLock(decimal value)
        {

            // Get xDocument Collection Reference
            var xDocumentDictionaryCollection = _db.GetCollection<BsonDocument>("Dictionary");

            var filter = Builders<BsonDocument>.Filter.Eq("_id", "graviexOrderBook");
            var document = xDocumentDictionaryCollection.Find(filter).FirstOrDefault();


            var orderBook = JsonConvert.DeserializeObject<OrderBookModel>(document.ToString());

            var asks = orderBook.Asks.OrderBy(l => l.Price);

            var btcTickerFilter = Builders<BsonDocument>.Filter.Eq("_id", "btcTicker");
            var btcTickerDocument = xDocumentDictionaryCollection.Find(btcTickerFilter).FirstOrDefault();

            var graviexTicker = JsonConvert.DeserializeObject<GraviexTickerModel>(btcTickerDocument.ToString());

            var sellprice = graviexTicker.Ticker.Sell;


            decimal totalAmount = value / sellprice;
            decimal TotalQty = 0;


            foreach (var item in asks)
            {
                if ((item.Quantity * item.Price) < totalAmount)
                {
                    totalAmount = totalAmount - (item.Quantity * item.Price);
                    TotalQty += item.Quantity;
                }
                else
                {


                    TotalQty += totalAmount / item.Price;
                    break;


                }


            }

            var fee = TotalQty * 0.05m;

            return Task.FromResult(Math.Round(TotalQty - fee));


        }


        public async Task<string> AddActionRequest(object request)
        {

            string jsonRequest = JsonConvert.SerializeObject(request);

            var dynamicObject = JsonConvert.DeserializeObject<dynamic>(jsonRequest);
            string data = JsonConvert.SerializeObject(dynamicObject["data"]);
 


            jsonRequest = JsonUtility.NormalizeJsonString(jsonRequest);

            if (dynamicObject["documentType"] != 1)
            {

                throw new NotImplementedException();

            }

            if (dynamicObject["keyAddress"] != null && dynamicObject["signature"] != null)
            {


                var dataObject = dynamicObject["data"];

                string dataObjectAsJson = Serialize(dataObject);

                var isValid = await _x42Client.VerifyMessageAsync(dynamicObject["keyAddress"], dataObjectAsJson, dynamicObject["signature"]);

                if (!isValid) {

                    throw new Exception("Invalid Signature");

                }
            }




            // Get xDocument Collection Reference
            var xDocument = _db.GetCollection<BsonDocument>("xDocumentPending");

            // Get xDocumentHashReference Collection Reference
            var xDocumentHashReference = _db.GetCollection<BsonDocument>("XDocumentHashReference");

            var Id = Guid.NewGuid();


            dynamicObject._id = Id;

            var dataObject = dynamicObject["data"];


            // Convert request to JSON string
            string json = Serialize(dataObject);

            // Calculate Document Hash
            var hash = HashString(json);


            BsonDocument xDocumentEntry
                = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(json);

            xDocument.InsertOne(xDocumentEntry);

            var xDocumentHashReferenceEntry = new BsonDocument
            {
                {"_id",  Id.ToString()  },
                {"hash",  hash.ToString()}
            };

            xDocumentHashReference.InsertOne(xDocumentHashReferenceEntry);

            return hash;

        }

        private string Serialize(object obj)
        {


            string serialized = JsonConvert.SerializeObject(obj, Formatting.Indented);

            return JsonUtility.NormalizeJsonString(serialized);

        }

        private string HashString(string text, string salt = "")
        {
            if (String.IsNullOrEmpty(text))
            {
                return String.Empty;
            }

            // Uses SHA256 to create the hash
            using (var sha = new System.Security.Cryptography.SHA256Managed())
            {
                // Convert the string to a byte array first, to be processed
                byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(text + salt);
                byte[] hashBytes = sha.ComputeHash(textBytes);

                // Convert back to a string, removing the '-' that BitConverter adds
                string hash = BitConverter
                    .ToString(hashBytes)
                    .Replace("-", String.Empty);

                return hash;
            }
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

    }
}