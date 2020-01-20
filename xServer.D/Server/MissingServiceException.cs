﻿using System;

namespace X42.Server
{
    /// <summary>
    ///     Exception thrown when a required service has not been registered into <see cref="IServerServiceProvider" />.
    /// </summary>
    public class MissingServiceException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="MissingDependencyException" /> class.
        /// </summary>
        /// <param name="missingServiceType">Type of the missing service.</param>
        public MissingServiceException(Type missingServiceType)
            : base($"The service {missingServiceType.FullName} has not been registered. Missing feature?")
        {
            MissingServiceType = missingServiceType;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MissingDependencyException" /> class.
        /// </summary>
        /// <param name="missingServiceType">Type of the missing service.</param>
        /// <param name="message">The message.</param>
        public MissingServiceException(Type missingServiceType, string message)
            : base(message)
        {
            MissingServiceType = missingServiceType;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MissingDependencyException" /> class.
        /// </summary>
        /// <param name="missingServiceType">Type of the missing service.</param>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public MissingServiceException(Type missingServiceType, string message, Exception innerException)
            : base(message, innerException)
        {
            MissingServiceType = missingServiceType;
        }

        /// <summary>
        ///     The Type of the missing service.
        /// </summary>
        public Type MissingServiceType { get; }
    }
}