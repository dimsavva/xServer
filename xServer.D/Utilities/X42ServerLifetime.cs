﻿using System;
using System.Threading;

namespace X42.Utilities
{
    /// <summary>
    ///     Allows consumers to perform cleanup during a graceful shutdown.
    /// </summary>
    public interface IX42ServerLifetime
    {
        /// <summary>
        ///     Triggered when the application host has fully started and is about to wait
        ///     for a graceful shutdown.
        /// </summary>
        CancellationToken ApplicationStarted { get; }

        /// <summary>
        ///     Triggered when the application host is performing a graceful shutdown.
        ///     Requests may still be in flight. Shutdown will block until this event completes.
        /// </summary>
        CancellationToken ApplicationStopping { get; }

        /// <summary>
        ///     Triggered when the application host is performing a graceful shutdown.
        ///     All requests should be complete at this point. Shutdown will block
        ///     until this event completes.
        /// </summary>
        CancellationToken ApplicationStopped { get; }

        /// <summary>Requests termination the current application.</summary>
        void StopApplication();
    }

    /// <summary>
    ///     Allows consumers to perform cleanup during a graceful shutdown.
    ///     Borrowed from asp.net core
    /// </summary>
    public class X42ServerLifetime : IX42ServerLifetime
    {
        private readonly CancellationTokenSource startedSource = new CancellationTokenSource();

        private readonly CancellationTokenSource stoppedSource = new CancellationTokenSource();

        private readonly CancellationTokenSource stoppingSource = new CancellationTokenSource();

        /// <summary>
        ///     Triggered when the application host has fully started and is about to wait
        ///     for a graceful shutdown.
        /// </summary>
        public CancellationToken ApplicationStarted => startedSource.Token;

        /// <summary>
        ///     Triggered when the application host is performing a graceful shutdown.
        ///     Request may still be in flight. Shutdown will block until this event completes.
        /// </summary>
        public CancellationToken ApplicationStopping => stoppingSource.Token;

        /// <summary>
        ///     Triggered when the application host is performing a graceful shutdown.
        ///     All requests should be complete at this point. Shutdown will block
        ///     until this event completes.
        /// </summary>
        public CancellationToken ApplicationStopped => stoppedSource.Token;

        /// <summary>
        ///     Signals the ApplicationStopping event and blocks until it completes.
        /// </summary>
        public void StopApplication()
        {
            CancellationTokenSource stoppingSource = this.stoppingSource;
            bool lockTaken = false;
            try
            {
                Monitor.Enter(stoppingSource, ref lockTaken);
                try
                {
                    this.stoppingSource.Cancel(false);
                }
                catch (Exception)
                {
                }
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(stoppingSource);
            }
        }

        /// <summary>
        ///     Signals the ApplicationStarted event and blocks until it completes.
        /// </summary>
        public void NotifyStarted()
        {
            try
            {
                startedSource.Cancel(false);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        ///     Signals the ApplicationStopped event and blocks until it completes.
        /// </summary>
        public void NotifyStopped()
        {
            try
            {
                stoppedSource.Cancel(false);
            }
            catch (Exception)
            {
            }
        }
    }
}