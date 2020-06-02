﻿using Microsoft.Extensions.DependencyInjection;
using x42.Configuration;
using x42.Feature.Setup;

namespace x42.Server
{
    /// <summary>
    ///     A class providing extension methods for <see cref="IServerBuilder" />.
    /// </summary>
    public static class ServerSettingsExtension
    {
        /// <summary>
        ///     Makes the x42 server builder use specific server settings.
        /// </summary>
        /// <param name="builder">x42 server builder to change server settings for.</param>
        /// <param name="serverSettings">Server settings to be used.</param>
        /// <returns>Interface to allow fluent code.</returns>
        public static IServerBuilder UseServerSettings(this IServerBuilder builder, ServerSettings serverSettings)
        {
            ServerBuilder serverBuilder = builder as ServerBuilder;
            serverBuilder.ServerSettings = serverSettings;
            serverBuilder.ServerNode = serverSettings.ServerNode;

            builder.ConfigureServices(service =>
            {
                service.AddSingleton(serverBuilder.ServerSettings);
                service.AddSingleton(serverBuilder.ServerNode);
            });

            return builder.UseBaseFeature();
        }

        /// <summary>
        ///     Makes the x42 server builder use the default server settings.
        /// </summary>
        /// <param name="builder">x42 server builder to change server settings for.</param>
        /// <returns>Interface to allow fluent code.</returns>
        public static IServerBuilder UseDefaultServerSettings(this IServerBuilder builder)
        {
            return builder.UseServerSettings(ServerSettings.Default(builder.ServerNode));
        }
    }
}