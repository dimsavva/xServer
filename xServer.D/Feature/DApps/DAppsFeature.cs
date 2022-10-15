﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x42.Feature.Setup;
using Microsoft.Extensions.Logging;
using x42.Feature.Metrics;
using x42.Server;
using x42.Configuration.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace x42.Feature.DApps
{
    public class DAppsFeature : ServerFeature
    {

        private readonly ILogger logger;

        public DAppsFeature(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger(GetType().FullName);

        }
        public override Task InitializeAsync()
        {
            logger.LogInformation("DApps Feature Initialized");
            return Task.CompletedTask;
        }
    }

    public static class DAppsBuilderExtension
    {
        /// <summary>
        ///     Adds Metrics components to the server.
        /// </summary>
        /// <param name="serverBuilder">The object used to build the current node.</param>
        /// <returns>The server builder, enriched with the new component.</returns>
        public static IServerBuilder UseDApps(this IServerBuilder serverBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<MetricsFeature>("dapps");

            serverBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<DAppsFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<DAppsFeature>();
                    });
            });

            return serverBuilder;
        }
    }

}
