﻿using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using x42.Controllers.Requests;
using x42.Feature.Database.Context;
using x42.Feature.Database.Tables;
using x42.Server.Results;
using x42.ServerNode;

namespace x42.Server
{
    public class SetupServer
    {
        private string ConnectionString { get; set; }

        public enum Status
        {
            NotStarted = 1,
            Started = 2,
            Complete = 3
        }

        public SetupServer(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public bool AddServerToSetup(SetupRequest setupRequest, string profileName)
        {
            bool result = false;

            using (X42DbContext dbContext = new X42DbContext(ConnectionString))
            {
                IQueryable<ServerData> serverNodes = dbContext.Servers;
                if (serverNodes.Count() == 0)
                {
                    ServerData serverData = new ServerData()
                    {
                        SignAddress = setupRequest.SignAddress,
                        ProfileName = profileName,
                        DateAdded = DateTime.UtcNow
                    };

                    var newRecord = dbContext.Add(serverData);
                    if (newRecord.State == EntityState.Added)
                    {
                        dbContext.SaveChanges();
                        result = true;
                    }
                }
            }
            return result;
        }

        public void UpdateServerProfile(string profileName)
        {
            using (X42DbContext dbContext = new X42DbContext(ConnectionString))
            {
                ServerData serverNode = dbContext.Servers.FirstOrDefault();
                if (serverNode != null)
                {
                    serverNode.ProfileName = profileName;

                    dbContext.SaveChanges();
                }
            }
        }

        public SetupStatusResult GetServerSetupStatus()
        {
            SetupStatusResult result = new SetupStatusResult() { ServerStatus = Status.NotStarted };

            using (X42DbContext dbContext = new X42DbContext(ConnectionString))
            {
                IQueryable<ServerData> servers = dbContext.Servers;
                if (servers.Count() > 0)
                {
                    result.ServerStatus = Status.Started;

                    var serverInfo = servers.FirstOrDefault();

                    if (serverInfo != null) {
                        IQueryable<ServerNodeData> serverNode = dbContext.ServerNodes.Where(s => s.ProfileName == serverInfo.ProfileName && s.Active);
                        if (serverNode.Count() > 0)
                        {
                            result.SignAddress = serverInfo.SignAddress;
                            result.ServerStatus = Status.Complete;
                            result.TierLevel = (Tier.TierLevel)serverNode.First().Tier;
                        }
                    }
                }
            }
            return result;
        }

        public string GetSignAddress()
        {
            string result = string.Empty;
            using (X42DbContext dbContext = new X42DbContext(ConnectionString))
            {
                IQueryable<ServerData> server = dbContext.Servers;
                if (server.Count() > 0)
                {
                    result = server.First().SignAddress;
                }
            }
            return result;
        }
    }
}
