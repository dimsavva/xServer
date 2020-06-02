﻿using System;
using System.Threading.Tasks;
using x42.Configuration;
using x42.Feature.Api;
using x42.Feature.Database;
using x42.Feature.X42Client;
using x42.Feature.Network;
using x42.ServerNode;
using x42.Protocol;
using x42.Server;
using x42.Utilities.Extensions;

namespace x42
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            try
            {
                ServerSettings serverSettings = new ServerSettings(new X42MainServerNode(), ProtocolVersion.PROTOCOL_VERSION, args: args);

                IxServer server = new ServerBuilder()
                    .UseServerSettings(serverSettings)
                    .UseX42Client()
                    .UseSql()
                    .UseApi()
                    .UseNetwork()
                    .Build();

                if (server != null) await server.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"There was a problem initializing xServer. Details: '{ex}'");
            }
        }
    }
}