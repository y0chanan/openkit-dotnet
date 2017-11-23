﻿using Dynatrace.OpenKit.Protocol;
using System;

namespace Dynatrace.OpenKit.Core.Communication
{
    /// <summary>
    /// Initial state for beacon sending.
    /// 
    /// The initial state is used to retrieve the configuration from the server and update the configuration.
    /// 
    /// Transitions to:
    /// <ul>
    ///     <li><see cref="BeaconSendingTerminalState"/> upon shutdown request</li>
    ///     <li><see cref="BeaconSendingTimeSyncState"/> if initial status request succeeded</li>
    /// </ul>
    /// </summary>
    internal class BeaconSendingInitState : AbstractBeaconSendingState
    {
        public const int MAX_INITIAL_STATUS_REQUEST_RETRIES = 5;
        public const int INITIAL_RETRY_SLEEP_TIME_MILLISECONDS = 1000;

        // delays between consecutive initial status requests
        internal static readonly int[] REINIT_DELAY_MILLISECONDS =
        {
            1 * 60 * 1000, // 1 minute in milliseconds
            5 * 60 * 1000, // 5 minutes in milliseconds
            15 * 60 * 1000, // 15 minutes in milliseconds
            1 * 60 * 60 * 1000, // 1 hour in millisecondsd
            2 * 60 * 60 * 1000 // 2 housrs in milliseconds
        };

        // current index into RE_INIT_DELAY_MILLISECONDS
        private int reinitializeDelayIndex = 0;

        internal BeaconSendingInitState() : base(false) { }

        internal override AbstractBeaconSendingState ShutdownState => new BeaconSendingTerminalState();

        internal override void OnInterrupted(IBeaconSendingContext context)
        {
            context.InitCompleted(false);
        }

        protected override void DoExecute(IBeaconSendingContext context)
        {
            StatusResponse statusResponse;
            while (true)
            {
                long currentTimestamp = context.CurrentTimestamp;
                context.LastOpenSessionBeaconSendTime = currentTimestamp;
                context.LastStatusCheckTime = currentTimestamp;

                statusResponse = BeaconSendingRequestUtil.SendStatusRequest(context, MAX_INITIAL_STATUS_REQUEST_RETRIES, INITIAL_RETRY_SLEEP_TIME_MILLISECONDS);
                if (context.IsShutdownRequested || statusResponse != null)
                {
                    // shutdown was requested or a status response was received
                    break;
                }

                // status request needs to be sent again after some delay
                context.Sleep(REINIT_DELAY_MILLISECONDS[reinitializeDelayIndex]);
                reinitializeDelayIndex = Math.Min(reinitializeDelayIndex + 1, REINIT_DELAY_MILLISECONDS.Length - 1); // ensure no out of bounds
            }

            if (context.IsShutdownRequested)
            {
                // shutdown was requested -> abort init with failure
                // transition to shutdown state is handled by base class
                context.InitCompleted(false);
            }
            else if (statusResponse != null)
            {
                // success -> continue with time sync
                context.HandleStatusResponse(statusResponse);
                context.CurrentState = new BeaconSendingTimeSyncState(true);
            }
        }

        private static bool RetryStatusRequest(IBeaconSendingContext context, StatusResponse statusResponse, int retry)
        {
            return statusResponse == null
                && (retry < MAX_INITIAL_STATUS_REQUEST_RETRIES)
                && !context.IsShutdownRequested;
                
        }
    }
}
