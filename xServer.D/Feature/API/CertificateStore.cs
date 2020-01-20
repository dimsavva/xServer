﻿using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace X42.Feature.Api
{
    public class CertificateStore : ICertificateStore
    {
        private readonly ILogger logger;

        public CertificateStore(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger(GetType().FullName);
        }

        /// <inheritdoc />
        public bool TryGet(string filePath, out X509Certificate2 certificate)
        {
            try
            {
                byte[] fileInBytes = File.ReadAllBytes(filePath);
                certificate = new X509Certificate2(fileInBytes);
                return true;
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to read certificate at {0} : {1}", filePath, e.Message);
                certificate = null;
                return false;
            }
        }
    }
}