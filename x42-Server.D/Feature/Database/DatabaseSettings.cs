﻿using System.Text;
using Microsoft.Extensions.Logging;
using X42.Configuration;
using X42.MasterNode;
using X42.Utilities;

namespace X42.Feature.Database
{
    /// <summary>
    ///     Configuration related to the database interface.
    /// </summary>
    public class DatabaseSettings
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>
        ///     Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="serverSettings">The node configuration.</param>
        public DatabaseSettings(ServerSettings serverSettings)
        {
            Guard.NotNull(serverSettings, nameof(serverSettings));

            logger = serverSettings.LoggerFactory.CreateLogger(typeof(DatabaseSettings).FullName);

            TextFileConfiguration config = serverSettings.ConfigReader;

            ConnectionString = config.GetOrDefault("connectionstring",
                "User ID=root;Password=myPassword;Host=localhost;Port=5432;Database=myDataBase;", logger);
        }

        /// <summary>
        ///     An address to use for the database.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        ///     Displays database help information on the console.
        /// </summary>
        /// <param name="masterNode">Not used.</param>
        public static void PrintHelp(MasterNodeBase masterNode)
        {
            ServerSettings defaults = ServerSettings.Default(masterNode);
            StringBuilder builder = new StringBuilder();

            builder.AppendLine("-connectionstring=<string>                     Database host.");

            defaults.Logger.LogInformation(builder.ToString());
        }

        /// <summary>
        ///     Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, MasterNodeBase masterNodeBase)
        {
            builder.AppendLine("####Database Settings####");
            builder.AppendLine("#Connection string for database.");
            builder.AppendLine("#connectionstring=<string>");
        }
    }
}