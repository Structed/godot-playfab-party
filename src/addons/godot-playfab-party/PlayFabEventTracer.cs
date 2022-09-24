/*
 * PlayFab Unity SDK
 *
 * Copyright (c) Microsoft Corporation
 *
 * MIT License
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this
 * software and associated documentation files (the "Software"), to deal in the Software
 * without restriction, including without limitation the rights to use, copy, modify, merge,
 * publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
 * to whom the Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
 * INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
 * PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
 * FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
 * OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */

#if !DISABLE_PLAYFABENTITY_API && !DISABLE_PLAYFABCLIENT_API
using System;
using System.Collections;
using System.Collections.Generic;
using PlayFab.Internal;
using UnityEngine;

namespace PlayFab.Party
{
    /// <summary>
    /// This class is used to send telemetry events to the PlayFab service
    /// </summary>
    internal sealed class PlayFabEventTracer : SingletonMonoBehaviour<PlayFabEventTracer>
    {
        private Guid gameSessionID;

        private Queue<EventsModels.EventContents> eventsRequests = new Queue<EventsModels.EventContents>();
        private Queue<EventsModels.EventContents> eventsPending = new Queue<EventsModels.EventContents>();

        private EventsModels.EntityKey entityKey = new EventsModels.EntityKey();
        private const string eventNamespace = "playfab.party";
        private const float delayBetweenEntityLoggedIn = 5.0f;
        private const int maxBatchSizeInEvents = 10;
        private long lastErrorTimeInMillisecond = GetCurrentTimeInMilliseconds();
        private int retryCount = 0;

        private PlayFabEventsInstanceAPI eventApi;

        private PlayFabEventTracer()
        {
            eventApi = new PlayFabEventsInstanceAPI(PlayFabSettings.staticPlayer);
        }

        /// <summary>
        /// Sets common properties associated with all the events
        /// </summary>
        private void SetCommonTelemetryProperties(Dictionary<string, object> payload)
        {
            payload["OSName"] = SystemInfo.operatingSystem;
            payload["DeviceMake"] = SystemInfo.deviceName;
            payload["DeviceModel"] = SystemInfo.deviceModel;
            payload["Platform"] = Application.platform;
            payload["AppName"] = Application.productName;
            payload["AppVersion"] = Application.version;
        }

        /// <summary>
        /// Gets the current ticks in milliseconds
        /// </summary>
        private static long GetCurrentTimeInMilliseconds()
        {
            // Ticks per millisecond = 10,000
            return (DateTime.UtcNow.Ticks / (10000));
        }

        /// <summary>
        /// Queues the initialization event.
        /// </summary>
        public void OnPlayFabMultiPlayerManagerInitialize()
        {
            gameSessionID = Guid.NewGuid();

            EventsModels.EventContents eventInfo = new EventsModels.EventContents();

            eventInfo.Name = "unity_client_initialization_completed";
            eventInfo.EventNamespace = eventNamespace;
            eventInfo.Entity = entityKey;
            eventInfo.OriginalTimestamp = DateTime.UtcNow;

            var payload = new Dictionary<string, object>();
            SetCommonTelemetryProperties(payload);
            payload["ClientInstanceId"] = gameSessionID;
            payload["PartyVersion"] = Version.PartyNativeVersion;
            payload["PartyUnityVersion"] = Version.PartyUnityVersion;
            payload["UnityVersion"] = Application.unityVersion;

            eventInfo.Payload = payload;
            if(entityKey.Id == null)
            {
                eventsPending.Enqueue(eventInfo);
                // we need to call this only once, during initialization, once logged in and entity has been retrieved, we will no longer call this.
                StartCoroutine(WaitUntilEntityLoggedIn(delayBetweenEntityLoggedIn));
            }
            else
            {
                eventsRequests.Enqueue(eventInfo);
            }
        }

        /// <summary>
        /// A coroutine to wait until we get an Entity Id after PlayFabLogin
        /// </summary>
        /// <param name="secondsBetweenWait">delay wait between checking whether Entity has logged in</param>
        private IEnumerator WaitUntilEntityLoggedIn(float secondsBetweenWait)
        {
            WaitForSeconds delay = new WaitForSeconds(secondsBetweenWait);

            while (entityKey.Id == null)
            {
                if (PlayFabAuthenticationAPI.IsEntityLoggedIn())
                {
                    entityKey.Id = PlayFab.PlayFabSettings.staticPlayer.EntityId;
                    entityKey.Type = PlayFab.PlayFabSettings.staticPlayer.EntityType;
                    break;
                }
                yield return delay;
            }
        }

        /// <summary>
        /// Queues errors for sending it server
        /// </summary>
        /// <param name="errorCode">error code associated with the error</param>
        /// <param name="type">Type of error defined in unity layer</param>
        public void OnPlayFabPartyError(uint errorCode, PlayFabMultiplayerManagerErrorType type)
        {
            EventsModels.EventContents eventInfo = new EventsModels.EventContents();
            // TODO update the table name for unity errors
            eventInfo.Name = "unity_client_api_error_occurred";
            eventInfo.EventNamespace = eventNamespace;
            eventInfo.Entity = entityKey;
            eventInfo.OriginalTimestamp = DateTime.UtcNow;

            var payload = new Dictionary<string, object>();
            SetCommonTelemetryProperties(payload);
            payload["ClientInstanceId"] = gameSessionID;
            payload["ErrorCode"] = errorCode;
            payload["ErrorType"] = type;
            eventInfo.Payload = payload;

            if (entityKey.Id == null)
            {
                eventsPending.Enqueue(eventInfo);
            }
            else
            {
                eventsRequests.Enqueue(eventInfo);
            }
        }

        /// <summary>
        /// Sends events to server.
        /// </summary>
        public void DoWork()
        {
            if (PlayFabSettings.staticPlayer.IsClientLoggedIn())
            {
                // The events which are sent without login will only be in this queue intially.
                // Once login is done, the count should always be 0.
                while (eventsPending.Count > 0)
                {
                    if(entityKey.Id == null)
                    {
                        return;
                    }
                    EventsModels.EventContents eventInfo = eventsPending.Dequeue();
                    eventInfo.Entity = entityKey;
                    eventsRequests.Enqueue(eventInfo);
                }

                long currentTime = GetCurrentTimeInMilliseconds();

                if(currentTime > lastErrorTimeInMillisecond + (retryCount * 1000))
                {
                    if (eventsRequests.Count > 0)
                    {
                        EventsModels.WriteEventsRequest request = new EventsModels.WriteEventsRequest();
                        request.Events = new List<EventsModels.EventContents>();

                        while ((eventsRequests.Count > 0) && (request.Events.Count < maxBatchSizeInEvents))
                        {
                            EventsModels.EventContents eventInfo = eventsRequests.Dequeue();
                            request.Events.Add(eventInfo);
                        }

                        if (request.Events.Count > 0)
                        {
#if !UNITY_EDITOR
                            eventApi.WriteTelemetryEvents(request, EventSentSuccessfulCallback, EventSentErrorCallback);
#endif
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Callback to handle successful server interaction.
        /// </summary>
        /// <param name="response">Server response</param>
        private void EventSentSuccessfulCallback(EventsModels.WriteEventsResponse response)
        {
            retryCount = 0;
        }

        /// <summary>
        /// Callback to handle unsuccessful server interaction.
        /// </summary>
        /// <param name="response">Server response</param>
        private void EventSentErrorCallback(PlayFabError response)
        {
            Debug.LogWarning("Failed to send session data. Error: " + response.GenerateErrorReport());
            // if we get APIClientRequestRateLimitExceeded then backoff and retry
            if(response.Error == PlayFabErrorCode.APIClientRequestRateLimitExceeded)
            {
                lastErrorTimeInMillisecond = GetCurrentTimeInMilliseconds();
                retryCount++;
            }
        }

        #region Unused MonoBehaviour compatibility  methods
        /// <summary>
        /// Unused
        /// Name mimics MonoBehaviour method, for ease of integration.
        /// </summary>
        public void OnEnable()
        {
            // add code sending events on enable
        }

        /// <summary>
        /// Unused
        /// Name mimics MonoBehaviour method, for ease of integration.
        /// </summary>
        public void OnDisable()
        {
            // add code sending events on disable
        }

        /// <summary>
        /// Unused
        /// Name mimics MonoBehaviour method, for ease of integration.
        /// </summary>
        public void OnDestroy()
        {
            // add code sending events on destroy
        }
        #endregion
    }
}
#endif
