﻿using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using x42.Configuration.Logging;
using x42.Feature.Database;
using x42.Feature.Setup;
using x42.Feature.X42Client;
using x42.ServerNode;
using x42.Server;
using x42.Utilities;
using x42.Configuration;
using System.Collections.Generic;
using x42.Feature.Database.Context;
using System.Linq;
using x42.Controllers.Results;
using x42.Feature.Database.Tables;
using x42.Controllers.Requests;
using Microsoft.EntityFrameworkCore;
using x42.Feature.Network;
using System.Threading;
using System;

namespace x42.Feature.Profile
{
    /// <inheritdoc />
    /// <summary>
    ///     Provides an ability to communicate with different network types.
    /// </summary>
    public class ProfileFeature : ServerFeature
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>
        ///     A cancellation token source that can cancel the node monitoring processes and is linked to the <see cref="IxServerLifetime.ApplicationStopping"/>.
        /// </summary>
        private CancellationTokenSource networkCancellationTokenSource;

        /// <summary>Global application life cycle control - triggers when application shuts down.</summary>
        private readonly IxServerLifetime serverLifetime;

        /// <summary>Factory for creating background async loop tasks.</summary>
        private readonly IAsyncLoopFactory asyncLoopFactory;

        /// <summary>Time in seconds between attempts to check the profile reservations.</summary>
        private readonly int updateProfileReservationsSeconds = 60;

        /// <summary>Time in seconds between attempts to relay profile information.</summary>
        private readonly int relayProfileSeconds = 2;

        /// <summary>Time in seconds between attempts to sync profile information from other nodes.</summary>
        private readonly int syncProfileSeconds = 60;

        private readonly ServerNodeBase network;
        private readonly DatabaseSettings databaseSettings;
        private readonly ServerSettings nodeSettings;
        private X42ClientSettings x42ClientSettings;
        private readonly X42ClientFeature x42FullNode;
        private readonly DatabaseFeatures database;
        private readonly NetworkFeatures networkFeatures;
        private readonly XServer xServer;
        private X42Node x42Client;

        public ProfileFeature(
            ServerNodeBase network,
            ServerSettings nodeSettings,
            ILoggerFactory loggerFactory,
            DatabaseSettings databaseSettings,
            X42ClientSettings x42ClientSettings,
            IxServerLifetime serverLifetime,
            IAsyncLoopFactory asyncLoopFactory,
            X42ClientFeature x42FullNode,
            DatabaseFeatures database,
            NetworkFeatures networkFeatures,
            XServer xServer
            )
        {
            this.network = network;
            this.nodeSettings = nodeSettings;
            logger = loggerFactory.CreateLogger(GetType().FullName);
            this.databaseSettings = databaseSettings;
            this.serverLifetime = serverLifetime;
            this.asyncLoopFactory = asyncLoopFactory;
            this.x42ClientSettings = x42ClientSettings;
            this.x42FullNode = x42FullNode;
            this.database = database;
            this.networkFeatures = networkFeatures;
            this.xServer = xServer;

            x42Client = new X42Node(x42ClientSettings.Name, x42ClientSettings.Address, x42ClientSettings.Port, logger, serverLifetime, asyncLoopFactory, false);
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            ProfileServices();

            logger.LogInformation("Profile Initialized");

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void ValidateDependencies(IServerServiceProvider services)
        {
            if (string.IsNullOrEmpty(x42ClientSettings.Name))
            {
                throw new ConfigurationException("x42Client Name setting must be set.");
            }

            if (x42ClientSettings.Port <= 0)
            {
                throw new ConfigurationException("x42Client Port setting must be set.");
            }

            if (x42ClientSettings.Address.AddressFamily == System.Net.Sockets.AddressFamily.Unknown)
            {
                throw new ConfigurationException("x42Client Address setting must be set, and a valid IP address.");
            }
        }

        private void ProfileServices()
        {
            this.networkCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(new[] { serverLifetime.ApplicationStopping });

            asyncLoopFactory.Run("Price.ProfileCheckService", async token =>
            {
                try
                {
                    if (xServer.Stats.TierLevel == Tier.TierLevel.Two && networkFeatures.IsServerReady())
                    {
                        await CheckReservedProfiles(this.networkCancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Exception: {0}", ex);
                    this.logger.LogTrace("(-)[UNHANDLED_EXCEPTION_PROFILE_CHECK]");
                    throw;
                }
            },
            this.networkCancellationTokenSource.Token,
            repeatEvery: TimeSpan.FromSeconds(this.updateProfileReservationsSeconds),
            startAfter: TimeSpans.TenSeconds);

            asyncLoopFactory.Run("Price.ProfileRelayService", async token =>
            {
                try
                {
                    if (xServer.Stats.TierLevel == Tier.TierLevel.Two && networkFeatures.IsServerReady())
                    {
                        await RelayProfiles(this.networkCancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Exception: {0}", ex);
                    this.logger.LogTrace("(-)[UNHANDLED_EXCEPTION_PROFILE_RELAY]");
                    throw;
                }
            },
            this.networkCancellationTokenSource.Token,
            repeatEvery: TimeSpan.FromSeconds(this.relayProfileSeconds),
            startAfter: TimeSpans.TenSeconds);

            asyncLoopFactory.Run("Price.ProfileSyncService", async token =>
            {
                try
                {
                    await SyncProfiles(this.networkCancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Exception: {0}", ex);
                    this.logger.LogTrace("(-)[UNHANDLED_EXCEPTION_PROFILE_SYNC]");
                    throw;
                }
            },
            this.networkCancellationTokenSource.Token,
            repeatEvery: TimeSpan.FromSeconds(this.syncProfileSeconds),
            startAfter: TimeSpans.TenSeconds);

        }


        /// <summary>
        ///     Relay profiles and reservations that have not been processed.
        /// </summary>
        private async Task RelayProfiles(CancellationToken cancellationToken)
        {
            using (X42DbContext dbContext = new X42DbContext(databaseSettings.ConnectionString))
            {
                var t2Servers = networkFeatures.GetAllTier2ConnectionInfo();
                var profileReservationsToRelay = dbContext.ProfileReservations.Where(pr => !pr.Relayed);
                foreach (var profileReservation in profileReservationsToRelay)
                {
                    foreach (var server in t2Servers)
                    {
                        await networkFeatures.RelayProfileReservation(cancellationToken, profileReservation, server);
                    }
                    profileReservation.Relayed = true;
                }
                dbContext.SaveChanges();
            }
        }

        /// <summary>
        ///     Check the reserved profiles, and process the paid ones, and remove the expired ones.
        /// </summary>
        private async Task CheckReservedProfiles(CancellationToken cancellationToken)
        {
            using (X42DbContext dbContext = new X42DbContext(databaseSettings.ConnectionString))
            {
                var profileReservations = dbContext.ProfileReservations;
                foreach (var profileReservation in profileReservations)
                {
                    if (Convert.ToInt64(networkFeatures.BestBlockHeight) <= profileReservation.ReservationExpirationBlock)
                    {
                        if (!ProfileExists(profileReservation.Name, profileReservation.KeyAddress, true))
                        {
                            await RegisterProfile(cancellationToken, profileReservation);
                        }
                    }
                    else
                    {
                        var priceLock = await networkFeatures.GetPriceLockFromT3(cancellationToken, profileReservation.PriceLockId);
                        if (priceLock.Status <= (int)PriceLock.Status.New)
                        {
                            dbContext.ProfileReservations.Remove(profileReservation);
                        }
                    }
                }
                dbContext.SaveChanges();
            }
        }

        private async Task RegisterProfile(CancellationToken cancellationToken, ProfileReservationData profileReservationData)
        {
            try
            {
                var priceLock = await networkFeatures.GetPriceLockFromT3(cancellationToken, profileReservationData.PriceLockId);
                if (priceLock.Status == (int)PriceLock.Status.Confirmed)
                {
                    var transaction = await networkFeatures.GetRawTransaction(priceLock.TransactionId, true);
                    if (transaction != null && transaction.BlockHeight > 0)
                    {
                        int blockConfirmed = (int)transaction.BlockHeight;
                        using (X42DbContext dbContext = new X42DbContext(databaseSettings.ConnectionString))
                        {
                            var profileCount = dbContext.Profiles.Where(p => p.Name == profileReservationData.Name || p.KeyAddress == profileReservationData.KeyAddress).Count();
                            if (profileCount == 0)
                            {
                                var newProfile = new ProfileData()
                                {
                                    KeyAddress = profileReservationData.KeyAddress,
                                    Name = profileReservationData.Name,
                                    PriceLockId = profileReservationData.PriceLockId,
                                    ReturnAddress = profileReservationData.ReturnAddress,
                                    Signature = profileReservationData.Signature,
                                    Relayed = profileReservationData.Relayed,
                                    BlockConfirmed = blockConfirmed,
                                    Status = (int)Status.Created
                                };
                                var newRecord = dbContext.Profiles.Add(newProfile);
                                if (newRecord.State == EntityState.Added)
                                {
                                    dbContext.SaveChanges();
                                    var profileHeight = database.dataStore.GetIntFromDictionary("ProfileHeight");
                                    if (blockConfirmed > profileHeight)
                                    {
                                        networkFeatures.SetProfileHeightOnSelf(blockConfirmed);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error During Profile Registration", ex);
            }
        }

        private bool ProfileExists(string name = "", string keyAddress = "", bool skipReservations = false)
        {
            int profileCount = 0;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(keyAddress))
            {
                if (!skipReservations)
                {
                    profileCount = database.dataStore.GetProfileReservationCountSearch(name, keyAddress);
                }
                profileCount += database.dataStore.GetProfileCountSearch(name, keyAddress);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                if (!skipReservations)
                {
                    profileCount = database.dataStore.GetProfileReservationCountByName(name);
                }
                profileCount += database.dataStore.GetProfileCountByName(name);
            }
            else if (!string.IsNullOrEmpty(keyAddress))
            {
                if (!skipReservations)
                {
                    profileCount = database.dataStore.GetProfileReservationCountByKeyAddress(keyAddress);
                }
                profileCount += database.dataStore.GetProfileCountByKeyAddress(keyAddress);
            }

            return profileCount > 0;
        }

        /// <summary>
        ///     Register a new profile.
        /// </summary>
        public async Task<ReserveProfileResult> ReserveProfile(ProfileReserveRequest profileRegisterRequest)
        {
            ReserveProfileResult result = new ReserveProfileResult();

            if (!ProfileExists(profileRegisterRequest.Name, profileRegisterRequest.KeyAddress))
            {
                bool isProfileKeyValid = await networkFeatures.IsProfileKeyValid(profileRegisterRequest.Name, profileRegisterRequest.KeyAddress, profileRegisterRequest.ReturnAddress, profileRegisterRequest.Signature);
                if (!isProfileKeyValid)
                {
                    result.Success = false;
                    result.ResultMessage = "Profile validation failed.";
                    return result;
                }

                // Price Lock ID does not exist, this is a new request, so let's create a price lock ID for it, and reserve the name.
                var profilePriceLockRequest = new CreatePriceLockRequest()
                {
                    DestinationAddress = networkFeatures.GetMyFeeAddress(),
                    RequestAmount = 5,      // $5
                    RequestAmountPair = 1,  // USD
                    ExpireBlock = 15
                };
                var newPriceLock = await networkFeatures.CreateNewPriceLock(profilePriceLockRequest);
                if (newPriceLock == null || string.IsNullOrEmpty(newPriceLock?.PriceLockId) || newPriceLock?.ExpireBlock <= 0)
                {
                    result.Success = false;
                    result.ResultMessage = "Failed to acquire a price lock";
                    return result;
                }
                int status = (int)Status.Reserved;
                var newProfile = new ProfileReservationData()
                {
                    Name = profileRegisterRequest.Name,
                    KeyAddress = profileRegisterRequest.KeyAddress,
                    ReturnAddress = profileRegisterRequest.ReturnAddress,
                    PriceLockId = newPriceLock.PriceLockId,
                    Signature = profileRegisterRequest.Signature,
                    Status = status,
                    ReservationExpirationBlock = newPriceLock.ExpireBlock,
                    Relayed = false
                };
                using (X42DbContext dbContext = new X42DbContext(databaseSettings.ConnectionString))
                {
                    var newRecord = dbContext.ProfileReservations.Add(newProfile);
                    if (newRecord.State == EntityState.Added)
                    {
                        dbContext.SaveChanges();
                        result.PriceLockId = newPriceLock.PriceLockId;
                        result.Status = status;
                        result.Success = true;
                    }
                    else
                    {
                        result.Status = (int)Status.Rejected;
                        result.ResultMessage = "Failed to add profile.";
                        result.Success = false;
                    }
                }
            }
            else
            {
                result.Status = (int)Status.Rejected;
                result.Success = false;
                result.ResultMessage = "Profile already exists.";
            }

            return result;
        }

        public async Task<bool> ReceiveProfileReservation(ReceiveProfileReserveRequest profileReserveSyncRequest)
        {
            bool result = false;
            using (X42DbContext dbContext = new X42DbContext(databaseSettings.ConnectionString))
            {
                var profileCount = dbContext.ProfileReservations.Where(p => p.Name == profileReserveSyncRequest.Name || p.KeyAddress == profileReserveSyncRequest.KeyAddress).Count();
                if (profileCount == 0)
                {
                    bool isProfileKeyValid = await networkFeatures.IsProfileKeyValid(profileReserveSyncRequest.Name, profileReserveSyncRequest.KeyAddress, profileReserveSyncRequest.ReturnAddress, profileReserveSyncRequest.Signature);
                    if (isProfileKeyValid)
                    {
                        var newProfile = new ProfileReservationData()
                        {
                            KeyAddress = profileReserveSyncRequest.KeyAddress,
                            Name = profileReserveSyncRequest.Name,
                            PriceLockId = profileReserveSyncRequest.PriceLockId,
                            ReturnAddress = profileReserveSyncRequest.ReturnAddress,
                            Signature = profileReserveSyncRequest.Signature,
                            Relayed = false,
                            Status = (int)Status.Reserved,
                            ReservationExpirationBlock = profileReserveSyncRequest.ReservationExpirationBlock
                        };
                        var newRecord = dbContext.ProfileReservations.Add(newProfile);
                        if (newRecord.State == EntityState.Added)
                        {
                            dbContext.SaveChanges();
                            result = true;
                        }
                    }
                }
            }
            return result;
        }

        private async Task<bool> AddProfile(ProfileData profileData)
        {
            bool result = false;
            using (X42DbContext dbContext = new X42DbContext(databaseSettings.ConnectionString))
            {
                bool isProfileKeyValid = await networkFeatures.IsProfileKeyValid(profileData.Name, profileData.KeyAddress, profileData.ReturnAddress, profileData.Signature);
                if (isProfileKeyValid)
                {
                    var newRecord = dbContext.Profiles.Add(profileData);
                    if (newRecord.State == EntityState.Added)
                    {
                        var saved = dbContext.SaveChanges();
                        if (saved > 0)
                        {
                            result = true;
                        }
                    }
                }
            }
            return result;
        }

        public List<ProfilesResult> GetProfiles(int fromBlock)
        {
            var result = new List<ProfilesResult>();
            var profileHeight = database.dataStore.GetIntFromDictionary("ProfileHeight");
            if (fromBlock < profileHeight)
            {
                var profiles = database.dataStore.GetFirstProfilesFromBlock(fromBlock, 10);
                foreach (var profile in profiles)
                {
                    var profileResult = new ProfilesResult()
                    {
                        KeyAddress = profile.KeyAddress,
                        Name = profile.Name,
                        PriceLockId = profile.PriceLockId,
                        BlockConfirmed = profile.BlockConfirmed,
                        Signature = profile.Signature,
                        ReturnAddress = profile.ReturnAddress,
                        Status = profile.Status
                    };
                    result.Add(profileResult);
                }
            }
            return result;
        }

        public async Task SyncProfiles(CancellationToken cancellationToken)
        {
            var profileHeight = database.dataStore.GetIntFromDictionary("ProfileHeight");
            var t2Servers = networkFeatures.GetAllTier2ConnectionInfo();
            foreach (var server in t2Servers)
            {
                int newHeight = 0;
                var profiles = await networkFeatures.GetProfiles(cancellationToken, server, profileHeight);
                while (profiles.Count > 0)
                {
                    foreach (var profile in profiles)
                    {
                        if (profile.BlockConfirmed > newHeight)
                        {
                            newHeight = profile.BlockConfirmed;
                        }
                        var newProfile = new ProfileData()
                        {
                            KeyAddress = profile.KeyAddress,
                            Name = profile.Name,
                            ReturnAddress = profile.ReturnAddress,
                            Signature = profile.Signature,
                            BlockConfirmed = profile.BlockConfirmed,
                            PriceLockId = profile.PriceLockId,
                            Relayed = true,
                            Status = (int)Status.Created
                        };
                        if (!ProfileExists(newProfile.Name, newProfile.KeyAddress, true))
                        {
                            var priceLock = await networkFeatures.GetPriceLockFromT3(cancellationToken, newProfile.PriceLockId);
                            if (priceLock != null)
                            {
                                var priceLockExists = AddCompletePriceLock(priceLock);
                                if (priceLockExists)
                                {
                                    await AddProfile(newProfile);
                                }
                            }
                        }
                    }
                    profiles = await networkFeatures.GetProfiles(cancellationToken, server, newHeight);
                }
                if (newHeight > profileHeight)
                {
                    networkFeatures.SetProfileHeightOnSelf(newHeight);
                }
            }
        }

        public bool AddCompletePriceLock(PriceLockResult priceLockResult)
        {
            bool result = false;
            if (Guid.TryParse(priceLockResult.PriceLockId, out Guid validPriceLockId))
            {
                var priceLock = networkFeatures.GetPriceLockData(validPriceLockId);
                if (priceLock != null)
                {
                    // TODO: Validate transaction ID, and amounts.
                    if (!string.IsNullOrEmpty(priceLock?.TransactionId))
                    {
                        return true;
                    }
                    else if (!string.IsNullOrEmpty(priceLockResult?.TransactionId))
                    {
                        using (X42DbContext dbContext = new X42DbContext(databaseSettings.ConnectionString))
                        {
                            var priceLockRecord = dbContext.PriceLocks.Where(p => p.PriceLockId == validPriceLockId).FirstOrDefault();
                            priceLockRecord.DestinationAddress = priceLockResult.DestinationAddress;
                            priceLockRecord.DestinationAmount = priceLockResult.DestinationAmount;
                            priceLockRecord.ExpireBlock = priceLockResult.ExpireBlock;
                            priceLockRecord.FeeAddress = priceLockResult.FeeAddress;
                            priceLockRecord.FeeAmount = priceLockResult.FeeAmount;
                            priceLockRecord.PayeeSignature = priceLockResult.PayeeSignature;
                            priceLockRecord.PriceLockId = validPriceLockId;
                            priceLockRecord.PriceLockSignature = priceLockResult.PriceLockSignature;
                            priceLockRecord.Relayed = true;
                            priceLockRecord.RequestAmount = priceLockResult.RequestAmount;
                            priceLockRecord.RequestAmountPair = priceLockResult.RequestAmountPair;
                            priceLockRecord.SignAddress = priceLockResult.SignAddress;
                            priceLockRecord.Status = priceLockResult.Status;
                            priceLockRecord.TransactionId = priceLockResult.TransactionId;

                            var saved = dbContext.SaveChanges();
                            if (saved > 0)
                            {
                                result = true;
                            }
                        }
                    }

                }
                else
                {
                    var newPriceLock = new PriceLockData()
                    {
                        DestinationAddress = priceLockResult.DestinationAddress,
                        DestinationAmount = priceLockResult.DestinationAmount,
                        ExpireBlock = priceLockResult.ExpireBlock,
                        FeeAddress = priceLockResult.FeeAddress,
                        FeeAmount = priceLockResult.FeeAmount,
                        PayeeSignature = priceLockResult.PayeeSignature,
                        PriceLockId = validPriceLockId,
                        PriceLockSignature = priceLockResult.PriceLockSignature,
                        Relayed = true,
                        RequestAmount = priceLockResult.RequestAmount,
                        RequestAmountPair = priceLockResult.RequestAmountPair,
                        SignAddress = priceLockResult.SignAddress,
                        Status = priceLockResult.Status,
                        TransactionId = priceLockResult.TransactionId
                    };
                    using (X42DbContext dbContext = new X42DbContext(databaseSettings.ConnectionString))
                    {
                        var newPriceLockRecord = dbContext.PriceLocks.Add(newPriceLock);
                        if (newPriceLockRecord.State == EntityState.Added)
                        {
                            var saved = dbContext.SaveChanges();
                            if (saved > 0)
                            {
                                if (!string.IsNullOrEmpty(newPriceLock?.TransactionId)) // TODO: Validate transaction ID, and amounts.
                                {
                                    result = true;
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        ///     Get profile.
        /// </summary>
        public ProfileResult GetProfile(ProfileRequest profileRequest)
        {
            ProfileResult result = null;

            if (!string.IsNullOrWhiteSpace(profileRequest.KeyAddress))
            {
                var profile = database.dataStore.GetProfileByKeyAddress(profileRequest.KeyAddress);
                if (profile != null)
                {
                    result = new ProfileResult()
                    {
                        KeyAddress = profile.KeyAddress,
                        Name = profile.Name,
                        Signature = profile.Signature,
                        PriceLockId = profile.PriceLockId,
                        Status = profile.Status,
                        ReturnAddress = profile.ReturnAddress,
                        BlockConfirmed = profile.BlockConfirmed
                    };
                }
                else
                {
                    var profileReservation = database.dataStore.GetProfileReservationByKeyAddress(profileRequest.KeyAddress);
                    if (profileReservation != null)
                    {
                        result = new ProfileResult()
                        {
                            KeyAddress = profileReservation.KeyAddress,
                            Name = profileReservation.Name,
                            Signature = profileReservation.Signature,
                            PriceLockId = profileReservation.PriceLockId,
                            Status = profileReservation.Status,
                            ReservationExpirationBlock = profileReservation.ReservationExpirationBlock
                        };
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(profileRequest.Name))
            {
                var profile = database.dataStore.GetProfileByName(profileRequest.Name);
                if (profile != null)
                {
                    result = new ProfileResult()
                    {
                        KeyAddress = profile.KeyAddress,
                        Name = profile.Name,
                        Signature = profile.Signature,
                        PriceLockId = profile.PriceLockId,
                        Status = profile.Status,
                        BlockConfirmed = profile.BlockConfirmed
                    };
                }
                else
                {
                    var profileReservation = database.dataStore.GetProfileReservationByName(profileRequest.Name);
                    if (profileReservation != null)
                    {
                        result = new ProfileResult()
                        {
                            KeyAddress = profileReservation.KeyAddress,
                            Name = profileReservation.Name,
                            Signature = profileReservation.Signature,
                            PriceLockId = profileReservation.PriceLockId,
                            Status = profileReservation.Status,
                            ReservationExpirationBlock = profileReservation.ReservationExpirationBlock
                        };
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    ///     A class providing extension methods for <see cref="ProfileFeature" />.
    /// </summary>
    public static class ProfileBuilderExtension
    {
        /// <summary>
        ///     Adds profile components to the server.
        /// </summary>
        /// <param name="serverBuilder">The object used to build the current node.</param>
        /// <returns>The server builder, enriched with the new component.</returns>
        public static IServerBuilder UseProfile(this IServerBuilder serverBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<ProfileFeature>("profile");

            serverBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<ProfileFeature>()
                    .DependOn<DatabaseFeatures>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<ProfileFeature>();
                    });
            });

            return serverBuilder;
        }
    }
}
