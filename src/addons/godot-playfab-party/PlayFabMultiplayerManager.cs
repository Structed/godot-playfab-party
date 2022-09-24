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

#if (MICROSOFT_GAME_CORE || UNITY_GAMECORE) && !UNITY_EDITOR
#define BUILD_XBL_PLUGIN
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using PartyCSharpSDK;
using PartyXBLCSharpSDK;
using PlayFab.ClientModels;
using PlayFab.Party._Internal;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.Compilation;
using UnityEditor;
#endif

namespace PlayFab.Party
{
    /// <summary>
    /// The primary class for the PlayFab Party APIs.
    /// </summary>
    public partial class PlayFabMultiplayerManager : MonoBehaviour
    {
        // Private variables
        private static PlayFabMultiplayerManager _multiplayerManager;
        private static LogLevelType _logLevel;
        private static bool _logLevelSetByUser;

        private IPlayFabChatPlatformPolicyProvider _platformPolicyProvider;
        private PlayFabLocalPlayer _localPlayer;
        private string _preferredLocalPlayerLanguageCode;
        private string _networkId;
        private string _generatedInvitationId;
        private List<PlayFabPlayer> _remotePlayers;
        private bool _translateChat = false;
        private AccessibilityMode _textToSpeechMode = AccessibilityMode.None;
        private AccessibilityMode _speechToTextMode = AccessibilityMode.None;
        private PARTY_HANDLE _partyHandle;
        private PARTY_NETWORK_HANDLE _networkHandle;
        private PARTY_LOCAL_USER_HANDLE _localUserHandle;
        private PARTY_DEVICE_HANDLE _localDeviceHandle;
        private PARTY_ENDPOINT_HANDLE _localEndPointHandle;
        private PARTY_CHAT_CONTROL_HANDLE _localChatControlHandle;
        private PARTY_NETWORK_DESCRIPTOR _networkDescriptor;
        private PARTY_SEND_MESSAGE_OPTIONS _defaultSendOptions;
        private PARTY_SEND_MESSAGE_QUEUING_CONFIGURATION _defaultQueuingConfiguration;

        private _InternalPlayFabMultiplayerManagerState _playFabMultiplayerManagerState = _InternalPlayFabMultiplayerManagerState.NotInitialized;
        private bool _isLeaveNetworkInProgress;
        private bool _isJoinNetworkInProgress;

        // The following arrays are used to limit garbage collection. See
        // the comment for the GetEndpointHandlesNoGC or GetChatControlHandlesNoGC
        // for details.
        private List<PARTY_ENDPOINT_HANDLE[]> _cachedSendMessageEndpointHandles;
        private List<PARTY_CHAT_CONTROL_HANDLE[]> _cachedSendMessageChatControlHandles;
        private PARTY_CHAT_CONTROL_HANDLE[] _cachedAllChatHandlesList;
        private List<PARTY_STATE_CHANGE> _partyStateChanges;
        private static PARTY_ENDPOINT_HANDLE[] _emptyEndpointHandlesArray = new PARTY_ENDPOINT_HANDLE[0] { };
        private static PARTY_CHAT_CONTROL_HANDLE[] _emptyChatControlHandlesArray = new PARTY_CHAT_CONTROL_HANDLE[0] { };

        private QueuedStartCreateAndJoinNetworkOp _queuedStartCreateAndJoinNetworkCreateLocalUserOp;
        private QueuedCreateAndJoinAfterLeaveNetworkOp _queuedCreateAndJoinAfterLeaveNetworkOp;
        private QueuedJoinNetworkOp _queuedJoinNetworkCreateLocalUserOp;
        private QueuedCompleteJoinAfterLeaveNetworkOp _queuedCompleteJoinAfterLeaveNetworkOp;

        private const int _DEVICES_PER_USER_COUNT = 1;
        private const int _ENDPOINTS_PER_DEVICE_COUNT = 1;
        private const int _USERS_PER_DEVICE = 1;
        private const string _NETWORK_ID_INVITE_AND_DESCRIPTOR_SEPERATOR = "|";
        private const uint _INTERNAL_EXCHANGE_MESSAGE_BUFFER_SIZE = 128;
        private const string _INTERNAL_EXCHANGE_REQUEST_MESSAGE_PREFIX = "PFP-";

        private const PARTY_CHAT_PERMISSION_OPTIONS _CHAT_PERMISSIONS_ALL =
            PARTY_CHAT_PERMISSION_OPTIONS.PARTY_CHAT_PERMISSION_OPTIONS_SEND_AUDIO |
            PARTY_CHAT_PERMISSION_OPTIONS.PARTY_CHAT_PERMISSION_OPTIONS_RECEIVE_AUDIO |
            PARTY_CHAT_PERMISSION_OPTIONS.PARTY_CHAT_PERMISSION_OPTIONS_RECEIVE_TEXT;

        private const PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS _PLATFORM_DEFAULT_CHAT_TRANSCRIPTION_OPTIONS = PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS.PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS_TRANSCRIBE_OTHER_CHAT_CONTROLS_WITH_MATCHING_LANGUAGES;

        private const string _ENTITY_TYPE_TITLE_PLAYER_ACCOUNT = "title_player_account";

        private const string _ErrorMessageNoUserLoggedIn = "No users logged in. You need to log in a user to PlayFab using the PlayFabClientAPI.LoginWithCustomID or similar API.";
        private const string _ErrorMessageMissingNetworkId = "networkId cannot be empty.";
        private const string _ErrorMessageMissingNetworkConfiguration = "networkConfiguration cannot be null.";
        private const string _ErrorMessageMissingPlayFabTitleId = "Missing Title ID. Please set your Title ID using PlayFab settings class or in the PlayFab Editor Extension.";
        private const string _ErrorMessagePartyAlreadyInitialized = "The Party DLL could not be unloaded. Please restart Unity to unload it.";
        private const string _ErrorMessagePlayerNotFound = "Player not found.";
        private const string _ErrorMessageEmptyDataMessagePayload = "Data message cannot be empty.";
        private const string _ErrorMessageTooManyRecipients = "Too many recipients.";
        private const string _ErrorMessageCannotCallAPINotConnectedToNetwork = "You need to connect to a network before you can call this method.";
        private const string _ErrorMessageMissingMultiplayerManagerPrefab = "PlayFabMultiplayerManager Prefab not found. You need to add the PlayFabMultiplayerManager prefab to your scene.";

        private const uint _c_ErrorFailedToFindResourceSpecified = 6;
        private const uint _c_ErrorAlreadyInitialized = 4101;
        private const uint _c_ErrorObjectIsBeingDestroyed = 4104;

        private List<WorkTask> _tasks = new List<WorkTask>();
        private WorkTask _runningTask = null;

        private bool gameObjectPersisted = false;

        private void Awake()
        {
#if UNITY_EDITOR && UNITY_2019_1_OR_NEWER
            CompilationPipeline.compilationStarted += CompilationPipelineCompilationStarted;
#endif
        }

        // Start is called before the first frame update
        private void Start()
        {
#if UNITY_SWITCH && !UNITY_EDITOR
            _SwitchInitialize();
#else
            _Initialize();
#endif
        }

#if UNITY_EDITOR
        private void CompilationPipelineCompilationStarted(object obj)
        {
            _CleanUp();
        }
#endif

        private void OnApplicationQuit()
        {
            _CleanUp();
        }

        void Update()
        {
#if UNITY_SWITCH && !UNITY_EDITOR
            _SwitchNetworkState();
#endif
            if (_playFabMultiplayerManagerState >= _InternalPlayFabMultiplayerManagerState.Initialized)
            {
                ProcessQueuedOperations();
                ProcessStateChanges();
                if (_platformPolicyProvider != null)
                {
                    _platformPolicyProvider.ProcessStateChanges();
                }
                PlayFabEventTracer.instance.DoWork();
            }
            
            if(HasTasks())
            {
                ProcessTask();
            }
        }

        private void OnDestroy()
        {
            _CleanUp();
        }

        /// <summary>
        /// Static function for getting a reference to the PlayFabMultiplayerManager.
        /// </summary>
        /// <returns>The singleton instance of the PlayFabMultiplayerManager</returns>
        public static PlayFabMultiplayerManager Get()
        {
            if (_multiplayerManager == null)
            {
                PlayFabMultiplayerManager[] playFabMultiplayerManagerInstances = FindObjectsOfType<PlayFabMultiplayerManager>();
                if (playFabMultiplayerManagerInstances.Length > 0)
                {
                    _multiplayerManager = playFabMultiplayerManagerInstances[0];
                    _multiplayerManager._Initialize();
                }
                else
                {
                    _LogError(_ErrorMessageMissingMultiplayerManagerPrefab);
                }
            }

            return _multiplayerManager;
        }

        //
        // Properties
        //

        /// <summary>
        /// Gets or sets the amount of logging currently enabled.
        /// </summary>
        public LogLevelType LogLevel
        {
            get
            {
                return _logLevel;
            }
            set
            {
                _logLevelSetByUser = true;
                _logLevel = value;
            }
        }

        /// <summary>
        /// Returns a reference to the local player.
        /// </summary>
        public PlayFabLocalPlayer LocalPlayer
        {
            get
            {
                return _localPlayer;
            }
        }

        /// <summary>
        /// Returns a Network ID that can be sent to other clients. The other clients can use this string to join
        /// the network created by this client. The NetworkId is populated when the OnNetworkJoined event fires.
        /// The NetworkId is cleared when the OnNetworkLeft event fires.
        /// </summary>
        public string NetworkId
        {
            get
            {
                return _networkId;
            }
        }

        /// <summary>
        /// Returns the current state of the multiplayer manager.
        /// </summary>
        public PlayFabMultiplayerManagerState State
        {
            get
            {
                PlayFabMultiplayerManagerState state;
                if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.Initialized)
                {
                    state = PlayFabMultiplayerManagerState.NotInitialized;
                }
                else if (_playFabMultiplayerManagerState == _InternalPlayFabMultiplayerManagerState.Initialized)
                {
                    state = PlayFabMultiplayerManagerState.Initialized;
                }
                else if (_playFabMultiplayerManagerState > _InternalPlayFabMultiplayerManagerState.Initialized &&
                    _playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
                {
                    state = PlayFabMultiplayerManagerState.ConnectingToNetwork;
                }
                else if (_playFabMultiplayerManagerState >= _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
                {
                    state = PlayFabMultiplayerManagerState.ConnectedToNetwork;
                }
                else
                {
                    state = PlayFabMultiplayerManagerState.NotInitialized;
                }
                return state;
            }
        }

        /// <summary>
        /// Contains a collection of the remote players currently joined to the network.
        /// </summary>
        public IList<PlayFabPlayer> RemotePlayers
        {
            get
            {
                return _remotePlayers.AsReadOnly();
            }
        }

        /// <summary>
        /// Gets or sets whether incoming chat messages should be translated to local player's language.
        /// Setting this property will have effect only when local player created or joined a party.
        /// </summary>
        public bool TranslateChat
        {
            get
            {
                return _translateChat;
            }
            set
            {
                if (value)
                {
                    SetTextChatOptions(PARTY_TEXT_CHAT_OPTIONS.PARTY_TEXT_CHAT_OPTIONS_TRANSLATE_TO_LOCAL_LANGUAGE);
                }
                else
                {
                    SetTextChatOptions(PARTY_TEXT_CHAT_OPTIONS.PARTY_TEXT_CHAT_OPTIONS_NONE);
                }

                _translateChat = value;
            }
        }

        /// <summary>
        /// Gets or sets whether speech to text is enabled.
        /// Setting this property will have effect only when local player created or joined a party.
        /// </summary>
        public AccessibilityMode SpeechToTextMode
        {
            get
            {
                return _speechToTextMode;
            }
            set
            {
                if (value == AccessibilityMode.Enabled)
                {
                    PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS transcriptionOptions =
                       PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS.PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS_TRANSLATE_TO_LOCAL_LANGUAGE |
                       PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS.PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS_TRANSCRIBE_OTHER_CHAT_CONTROLS_WITH_MATCHING_LANGUAGES |
                       PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS.PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS_TRANSCRIBE_OTHER_CHAT_CONTROLS_WITH_NON_MATCHING_LANGUAGES;
                    SetTranscriptionOptions(transcriptionOptions);
                }
                else if (value == AccessibilityMode.None)
                {
                    SetTranscriptionOptions(PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS.PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS_NONE);
                }
                else
                {
                    // value is AccessibilityMode.PlatformDefault
                    if (_platformPolicyProvider != null)
                    {
                        SetTranscriptionOptions(_platformPolicyProvider.GetPlatformUserChatTranscriptionPreferences());
                    }
                    else
                    {
                        SetTranscriptionOptions(_PLATFORM_DEFAULT_CHAT_TRANSCRIPTION_OPTIONS);
                    }
                }

                _speechToTextMode = value;
            }
        }

        /// <summary>
        /// Gets or sets whether text to speech is enabled.
        /// Note: if set to PlatformDefault, text to speech will only be enabled if local player's text to speech preferences are enabled.
        /// </summary>
        public AccessibilityMode TextToSpeechMode
        {
            get
            {
                return _textToSpeechMode;
            }
            set
            {
                _textToSpeechMode = value;
            }
        }

        //
        // Events
        //

        /// <summary>
        /// An event that is raised when the local player has joined the network.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="networkId">The identifier of the network the player joined.</param>
        public delegate void OnNetworkJoinedHandler(object sender, string networkId);
        public event OnNetworkJoinedHandler OnNetworkJoined;

        /// <summary>
        /// An event that is raised when the local player has left the network.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="networkId">The identifier of the network the player has left.</param>
        public delegate void OnNetworkLeftHandler(object sender, string networkId);
        public event OnNetworkLeftHandler OnNetworkLeft;

        /// <summary>
        /// An event that is raised when a remote player has joined the network.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="player">The new player who has joined the network.</param>
        public delegate void OnRemotePlayerJoinedHandler(object sender, PlayFabPlayer player);
        public event OnRemotePlayerJoinedHandler OnRemotePlayerJoined;

        /// <summary>
        /// An event that is raised when a remote player has left the network.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="player">The player who has left the network.</param>
        public delegate void OnRemotePlayerLeftHandler(object sender, PlayFabPlayer player);
        public event OnRemotePlayerLeftHandler OnRemotePlayerLeft;

        /// <summary>
        /// Event that is fired when the Network changes. When this event fires you
        /// can move all of the players to the new network, specified in the newNetworkID.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="networkID">The identifier for the new network</param>
        public delegate void OnNetworkChangedHandler(object sender, string newNetworkId);
        public event OnNetworkChangedHandler OnNetworkChanged;

        /// <summary>
        /// An event that is raised when a chat message is received.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="from">The player the message was sent from.</param>
        /// <param name="message">The contents of the message.</param>
        /// <param name="type">A parameter that specifies the type of message.</param>
        public delegate void OnChatMessageReceivedHandler(object sender, PlayFabPlayer from, string message, ChatMessageType type);
        public event OnChatMessageReceivedHandler OnChatMessageReceived;

        /// <summary>
        /// An event that is raised when a data message is received.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="from">The player who sent the message.</param>
        /// <param name="buffer">The data contents of the message.</param>
        public delegate void OnDataMessageReceivedHandler(object sender, PlayFabPlayer from, byte[] buffer);
        public event OnDataMessageReceivedHandler OnDataMessageReceived;

        /// <summary>
        /// A more advanced version of the OnDataMessageReceived event that avoids copies.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="from">The player who sent the message.</param>
        /// <param name="buffer">The data contents of the message.</param>
        public delegate void OnDataMessageReceivedNoCopyHandler(object sender, PlayFabPlayer from, IntPtr buffer, uint bufferSize);
        public event OnDataMessageReceivedNoCopyHandler OnDataMessageNoCopyReceived;

        /// <summary>
        /// An event that is raised when there is an error.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="args">An object containing the details of the error.</param>
        public delegate void OnErrorEventHandler(object sender, PlayFabMultiplayerManagerErrorArgs args);
        public event OnErrorEventHandler OnError;

        //
        // Methods
        //

        /// <summary>
        /// Resumes Party after it has been suspended.
        /// </summary>
        public void Resume()
        {
            _LogInfo("PlayFabMultiplayerManager:Resume()");
            InitializeImpl();
        }

        /// <summary>
        /// Suspends execution of Party.
        /// </summary>
        public void Suspend()
        {
            _LogInfo("PlayFabMultiplayerManager:Suspend()");
            CleanUpImpl();
            _tasks.Clear();
            _runningTask = null;
        }

        /// <summary>
        /// Creates a network for players to join and send the other players chat and data messages.
        /// </summary>
        public void CreateAndJoinNetwork()
        {
            PlayFabNetworkConfiguration defaultNetworkConfiguration = new PlayFabNetworkConfiguration();
            defaultNetworkConfiguration.MaxPlayerCount = PartyConstants.c_maxNetworkConfigurationMaxDeviceCount;
            CreateAndJoinNetwork(defaultNetworkConfiguration);
        }

        /// <summary>
        /// Creates a network for players to join and send the other players chat and data messages.
        /// </summary>
        /// <param name="networkConfiguration">A set of properties that specify the type of network to create.</param>
        public void CreateAndJoinNetwork(PlayFabNetworkConfiguration networkConfiguration)
        {
            CreateAndJoinNetworkImplStart(networkConfiguration);
        }

        /// <summary>
        /// Joins this player to the specified network.
        /// </summary>
        /// <param name="networkId">A string with information necessary for a player to join a network.</param>
        public void JoinNetwork(string networkId)
        {
            JoinNetworkImplStart(networkId);
        }

        /// <summary>
        /// Causes this player to leave the network.
        /// </summary>
        public void LeaveNetwork()
        {
            LeaveNetworkImpl(true);
        }

        /// <summary>
        /// Broadcasts a data message to all players.
        /// </summary>
        /// <param name="buffer">A buffer containing the data to send.</param>
        public void SendDataMessageToAllPlayers(byte[] buffer)
        {
            _SendDataMessageToAllPlayers(buffer);
        }

        /// <summary>
        /// A more advanced method for sending data messages, allowing the developer more
        /// control over how the message is sent.
        /// </summary>
        /// <param name="buffer">A buffer with the data to send.</param>
        /// <param name="recipients">The players to send the data message to. If the collection of players is empty, the data message will be broadcast to all players.</param>
        /// <param name="deliveryOption">Options specifying how the message will be delivered.</param>
        /// <returns>An indicator whether sending a data message was successful.</returns>
        public bool SendDataMessage(byte[] buffer, IEnumerable<PlayFabPlayer> recipients, DeliveryOption deliveryOption)
        {
            return _SendDataMessage(buffer, recipients, deliveryOption);
        }

        /// <summary>
        /// The most advanced method for sending data messages, allowing the developer more
        /// control over how the message is sent.
        /// </summary>
        /// <param name="buffer">A pointer to the buffer containing the data to send.</param>
        /// <param name="bufferSize">The size of the buffer.</param>
        /// <param name="recipients">The players to send the data message to. If the collection of players is empty, the data message will be broadcast to all players.</param>
        /// <param name="deliveryOption">Options specifying how the message will be delivered.</param>
        public void SendDataMessage(IntPtr buffer, uint bufferSize, IEnumerable<PlayFabPlayer> recipients, DeliveryOption deliveryOption)
        {
            _SendDataMessage(buffer, bufferSize, recipients, deliveryOption);
        }

        /// <summary>
        /// Broadcasts a text message to all players. This API will send the message with guaranteed reliability and guaranteed sequential order.
        /// </summary>
        /// <param name="message">The contents of the chat message.</param>
        public void SendChatMessageToAllPlayers(string message)
        {
            _SendChatMessageToAllPlayers(message);
        }

        /// <summary>
        /// Sends a chat message to a specific list of players.
        /// </summary>
        /// <param name="message">The contents of the chat message.</param>
        /// <param name="recipients">The recipients of the chat message.</param>
        public void SendChatMessage(string message, IEnumerable<PlayFabPlayer> recipients)
        {
            _SendChatMessage(message, recipients);
        }

        /// <summary>
        /// Updates the Entity token for the current local user.
        /// </summary>
        /// <param name="entityToken">The Entity token associated with the local user.</param>
        public void UpdateEntityToken(string entityToken)
        {
            if (_localUserHandle != null)
            {
                PartySucceeded(SDK.PartyLocalUserUpdateEntityToken(
                                _localUserHandle,
                                entityToken
                                ));
                _localPlayer._entityToken = entityToken;
            }
        }

        // Helper methods
        internal static void _LogError(string message)
        {
            if (_logLevel != LogLevelType.None)
            {
                Debug.LogError(message);
            }
        }

        internal static void _LogError(uint code)
        {
            _LogError(code, PlayFabMultiplayerManagerErrorType.Error);
        }

        internal static void _LogError(uint code, PlayFabMultiplayerManagerErrorType type)
        {
            string errorMessage = string.Empty;
            uint getErrorCodeError = SDK.PartyGetErrorMessage(code, out errorMessage);
            if (PartyError.FAILED(getErrorCodeError))
            {
                errorMessage = "Unknown error.";
            }
            PlayFabMultiplayerManager playFabMultiplayerManager = Get();
            if (playFabMultiplayerManager.OnError != null)
            {
                PlayFabMultiplayerManagerErrorArgs args = new PlayFabMultiplayerManagerErrorArgs((int)code, errorMessage, type);
                playFabMultiplayerManager.OnError(playFabMultiplayerManager, args);
            }
            PlayFabEventTracer.instance.OnPlayFabPartyError(code, type);
            _LogError(errorMessage);
        }

        internal static void _LogError(uint code, string message, PlayFabMultiplayerManagerErrorArgs args)
        {
            PlayFabMultiplayerManager playFabMultiplayerManager = Get();
            if (playFabMultiplayerManager.OnError != null)
            {
                playFabMultiplayerManager.OnError(playFabMultiplayerManager, args);
            }
            _LogError(message);
        }

        internal static void _LogWarning(string warningMessage)
        {
            if (_logLevel < LogLevelType.Verbose)
            {
                return;
            }
            Debug.LogWarning(warningMessage);
        }

        internal static void _LogInfo(string infoMessage)
        {
            if (_logLevel < LogLevelType.Verbose)
            {
                return;
            }
            Debug.Log(infoMessage);
        }

        internal bool _StartsWithSequence(byte[] buffer, byte[] sequence)
        {
            bool startsWithSequence = true;
            if (buffer.Length > sequence.Length + 1)
            {
                for (int i = 0; i < sequence.Length; i++)
                {
                    if (buffer[i] != sequence[i])
                    {
                        startsWithSequence = false;
                        break;
                    }
                }
            }
            else
            {
                startsWithSequence = false;
            }
            return startsWithSequence;
        }

        private bool IsInternalMessage(IntPtr messageBuffer, uint messageSize)
        {
            if (messageSize > 0 &&
                messageSize < _INTERNAL_EXCHANGE_MESSAGE_BUFFER_SIZE)
            {
                byte[] internalXuidExchangeMessageBuffer = new byte[_INTERNAL_EXCHANGE_MESSAGE_BUFFER_SIZE];
                Marshal.Copy(messageBuffer, internalXuidExchangeMessageBuffer, 0, (int)messageSize);
                return _StartsWithSequence(internalXuidExchangeMessageBuffer, Encoding.ASCII.GetBytes(_INTERNAL_EXCHANGE_REQUEST_MESSAGE_PREFIX));
            }
            return false;
        }

        private PlayFabPlayer GetPlayerByEntityId(string entityId)
        {
            if (_remotePlayers != null)
            {
                foreach (PlayFabPlayer remotePlayer in _remotePlayers)
                {
                    if (remotePlayer.EntityKey.Id == entityId)
                    {
                        return remotePlayer;
                    }
                }
            }
            return null;
        }

        // The following functions allow us to convert between the API which takes a list of PlayFabPlayers
        // and convert to an array of either PARTY_ENDPOINT_HANDLE or PARTY_CHAT_CONTROL_HANDLE without
        // allocating a new array each time.
        private PARTY_ENDPOINT_HANDLE[] EndPointHandlesFromPlayFabPlayerListNoGC(IEnumerable<PlayFabPlayer> playerList)
        {
            int playerListCount = playerList.Count();
            if (playerListCount == 0)
            {
                return _emptyEndpointHandlesArray;
            }

            int currentEndpointHandlesListSize = _cachedSendMessageEndpointHandles.Count();

            // If our cached list of endpoint handles lists is too small, then increase the size.
            if (currentEndpointHandlesListSize < playerListCount)
            {
                for (int i = currentEndpointHandlesListSize + 1; i <= playerListCount; i++)
                {
                    _cachedSendMessageEndpointHandles.Add(new PARTY_ENDPOINT_HANDLE[i]);
                }
            }
            for (int i = 0; i < playerList.Count(); i++)
            {
                _cachedSendMessageEndpointHandles[playerListCount - 1][i] = playerList.ElementAt(i)._endPointHandle;
            }
            return _cachedSendMessageEndpointHandles[playerListCount - 1];
        }

        private PARTY_CHAT_CONTROL_HANDLE[] ChatControlHandlesFromPlayFabPlayerListNoGC(IEnumerable<PlayFabPlayer> playerList)
        {
            int playerListCount = playerList.Count();
            if (playerListCount == 0)
            {
                return _emptyChatControlHandlesArray;
            }

            int currentSendMessageChatControlHandlesListSize = _cachedSendMessageChatControlHandles.Count();

            // If our cached list of chat control handles lists is too small, then increase the size.
            if (currentSendMessageChatControlHandlesListSize < playerListCount)
            {
                for (int i = currentSendMessageChatControlHandlesListSize + 1; i <= playerListCount; i++)
                {
                    _cachedSendMessageChatControlHandles.Add(new PARTY_CHAT_CONTROL_HANDLE[i]);
                }
            }
            for (int i = 0; i < playerList.Count(); i++)
            {
                _cachedSendMessageChatControlHandles[playerListCount - 1][i] = playerList.ElementAt(i)._chatControlHandle;
            }
            return _cachedSendMessageChatControlHandles[playerListCount - 1];
        }

        private void _Initialize()
        {
            _LogInfo("PlayFabMultiplayerManager:_Initialize()");
            InitializeImpl();
        }

#if UNITY_EDITOR
        private void HandlePlayModeStateChanged(PlayModeStateChange args)
        {
            // The following is equivalent to the case of exiting play mode.
            if (!EditorApplication.isPaused &&
                EditorApplication.isPlaying &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _CleanUp();
            }
        }
#endif

        private void _CleanUp()
        {
            _LogInfo("PlayFabMultiplayerManager:_CleanUp()");
            CleanUpImpl();
        }

        private void InitializeImpl()
        {
            if (_playFabMultiplayerManagerState > _InternalPlayFabMultiplayerManagerState.NotInitialized)
            {
                return;
            }
            _playFabMultiplayerManagerState = _InternalPlayFabMultiplayerManagerState.PendingInitialization;

            if (!_logLevelSetByUser)
            {
                _logLevel = LogLevelType.Minimal;
            }

#if UNITY_EDITOR
            // We always clear the old party handle between play and pause to make sure
            // the developer is in a good state.
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            string partyHandleValueAsString = EditorPrefs.GetString("_microsoftPlayFabPartyHandle", string.Empty);
            if (!string.IsNullOrEmpty(partyHandleValueAsString))
            {
                long partyHandleValue = Convert.ToInt64(partyHandleValueAsString);
                PARTY_HANDLE oldPartyHandle = new PARTY_HANDLE(partyHandleValue);
                bool cleanupSucceeded = PartySucceeded(SDK.PartyCleanup(oldPartyHandle));
                if (cleanupSucceeded)
                {
                    EditorPrefs.DeleteKey("_microsoftPlayFabPartyHandle");
                }
            }
#endif

#if BUILD_XBL_PLUGIN
            _platformPolicyProvider = PlayFabChatXboxLivePolicyProvider.Get();
#else
            _platformPolicyProvider = null;
#endif

            _defaultSendOptions =
            PARTY_SEND_MESSAGE_OPTIONS.PARTY_SEND_MESSAGE_OPTIONS_GUARANTEED_DELIVERY |
            PARTY_SEND_MESSAGE_OPTIONS.PARTY_SEND_MESSAGE_OPTIONS_SEQUENTIAL_DELIVERY |
            PARTY_SEND_MESSAGE_OPTIONS.PARTY_SEND_MESSAGE_OPTIONS_COALESCE_OPPORTUNISTICALLY;

            _defaultQueuingConfiguration = new PARTY_SEND_MESSAGE_QUEUING_CONFIGURATION
            {
                Priority = Convert.ToSByte(PartyConstants.c_defaultSendMessageQueuingPriority),
                IdentityForCancelFilters = 0,
                TimeoutInMilliseconds = 0
            };

            _localPlayer = new PlayFabLocalPlayer();
            _remotePlayers = new List<PlayFabPlayer>();
            _partyStateChanges = new List<PARTY_STATE_CHANGE>();
            _cachedSendMessageEndpointHandles = new List<PARTY_ENDPOINT_HANDLE[]>();
            _cachedSendMessageChatControlHandles = new List<PARTY_CHAT_CONTROL_HANDLE[]>();

            if (!gameObjectPersisted)
            {
                // On our first call we want to make sure we mark this game object with DontDestroyOnLoad so it
                // can persist across scenes but only need to call it that first time and not any of the other times we
                // might re-initialize for example to handle suspend/resume.
                // The PlayFabMultiplayerManager is a singleton and exists across scenes for convenience.

                gameObjectPersisted = true;
                DontDestroyOnLoad(gameObject);
            }
            string titleId = PlayFabSettings.staticSettings.TitleId;
            if (string.IsNullOrEmpty(titleId))
            {
                _LogError(_ErrorMessageMissingPlayFabTitleId);
            }
            uint errorCode = SDK.PartyInitialize(titleId, out _partyHandle);
            if (PartySucceeded(errorCode))
            {
                _playFabMultiplayerManagerState = _InternalPlayFabMultiplayerManagerState.Initialized;
            }
            else
            {
                if (errorCode == _c_ErrorAlreadyInitialized)
                {
                    _LogError(_ErrorMessagePartyAlreadyInitialized);
                }
#if UNITY_EDITOR
                if (_partyHandle != null)
                {
                    // We need to store the party handle so we can properly clean up the party session.
                    EditorPrefs.SetString("_microsoftPlayFabPartyHandle", _partyHandle.GetHandleValue().ToString());
                }
#endif
            }
            PlayFabEventTracer.instance.OnPlayFabMultiPlayerManagerInitialize();
        }

        private void CleanUpImpl()
        {
            if (_playFabMultiplayerManagerState <= _InternalPlayFabMultiplayerManagerState.NotInitialized)
            {
                return;
            }

            if (_partyHandle != null)
            {
                if (PartySucceeded(SDK.PartyCleanup(_partyHandle)))
                {
                    _playFabMultiplayerManagerState = _InternalPlayFabMultiplayerManagerState.NotInitialized;
                }
            }

            _isLeaveNetworkInProgress = false;
            _isJoinNetworkInProgress = false;

            _queuedStartCreateAndJoinNetworkCreateLocalUserOp.queued = false;
            _queuedCreateAndJoinAfterLeaveNetworkOp.queued = false;
            _queuedJoinNetworkCreateLocalUserOp.queued = false;
            _queuedCompleteJoinAfterLeaveNetworkOp.queued = false;

            if (_platformPolicyProvider != null)
            {
                _platformPolicyProvider.CleanUp();
            }

            _defaultQueuingConfiguration = null;
            _remotePlayers = null;
            _cachedSendMessageEndpointHandles = null;
            _cachedSendMessageChatControlHandles = null;
            _cachedAllChatHandlesList = null;
            _partyStateChanges = null;
            _localPlayer = null;
            _partyHandle = null;
            _localUserHandle = null;
            _networkDescriptor = null;
            _generatedInvitationId = null;
            _networkHandle = null;
            _localDeviceHandle = null;
            _localEndPointHandle = null;
            _localChatControlHandle = null;
        }

        private PARTY_NETWORK_DESCRIPTOR GetNetworkDescriptorFromNetworkId(string networkId)
        {
            _LogInfo("PlayFabMultiplayerManager:GetNetworkDescriptorFromNetworkId()");

            PARTY_NETWORK_DESCRIPTOR partyNetworkDescriptor = null;
            int indexOfSeperator = networkId.IndexOf(_NETWORK_ID_INVITE_AND_DESCRIPTOR_SEPERATOR);
            if (indexOfSeperator != -1)
            {
                _generatedInvitationId = networkId.Substring(0, indexOfSeperator);
                string networkDescriptorString = networkId.Substring(indexOfSeperator + 1);
                PartySucceeded(SDK.PartyDeserializeNetworkDescriptor(networkDescriptorString, out partyNetworkDescriptor));
            }

            return partyNetworkDescriptor;
        }

        private void ProcessQueuedOperations()
        {
            if (_playFabMultiplayerManagerState <= _InternalPlayFabMultiplayerManagerState.NotInitialized)
            {
                return;
            }

            if (_queuedStartCreateAndJoinNetworkCreateLocalUserOp.queued ||
                _queuedJoinNetworkCreateLocalUserOp.queued)
            {
                if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.LoginRequestIssued)
                {
                    if (_platformPolicyProvider != null)
                    {
                        _platformPolicyProvider.SignIn();
                    }
                    else
                    {
                        // We check if we are already authenticated to PlayFab. If yes, then we use
                        // the logged-in user.
                        if (PlayFabAuthenticationAPI.IsEntityLoggedIn())
                        {
                            AuthenticationModels.GetEntityTokenRequest request = new AuthenticationModels.GetEntityTokenRequest();
                            PlayFabAuthenticationAPI.GetEntityToken(request, GetEntityTokenCompleted, GetEntityTokenFailed);
                        }
                        else
                        {
                            _LogError(_ErrorMessageNoUserLoggedIn);
                        }
                    }

                    _playFabMultiplayerManagerState = _InternalPlayFabMultiplayerManagerState.LoginRequestIssued;
                }
            }
            if (_platformPolicyProvider != null)
            {
                _platformPolicyProvider.ProcessQueuedOperations();
            }
        }

        private void GetEntityTokenCompleted(AuthenticationModels.GetEntityTokenResponse response)
        {
            _LogInfo("PlayFabMultiplayerManager:GetEntityTokenCompleted(), EntityID: " + response.Entity.Id);

            EntityKey entityKey = new EntityKey()
            {
                Id = response.Entity.Id,
                Type = response.Entity.Type
            };
            _CreateLocalUser(entityKey, response.EntityToken);
        }

        internal void _CreateLocalUser(EntityKey entityKey, string entityToken)
        {
            _LogInfo("PlayFabMultiplayerManager:_CreateLocalUser(), EntityID: " + entityKey.Id);

            PartySucceeded(SDK.PartyGetLocalDevice(_partyHandle, out _localDeviceHandle));

            _localPlayer._entityToken = entityToken;
            _localPlayer._SetEntityKey(entityKey);
            uint errorCode = PartyError.Success;
            if (_localUserHandle == null)
            {
                errorCode = SDK.PartyCreateLocalUser(
                                _partyHandle,
                                entityKey.Id,
                                _localPlayer._entityToken,
                                out _localUserHandle
                                );
            }

            if (PartySucceeded(errorCode))
            {
                // Check if the user has already been associated, then don't create a new chat control.
                PARTY_LOCAL_USER_HANDLE existingUserHandle = null;
                if (_localChatControlHandle != null)
                {
                    PartySucceeded(SDK.PartyChatControlGetLocalUser(
                                        _localChatControlHandle,
                                        out existingUserHandle
                                    ));
                }
                if (existingUserHandle == null)
                {
                    PartySucceeded(SDK.PartyDeviceCreateChatControl(
                                        _localDeviceHandle,
                                        _localUserHandle,
                                        LocalPlayer._preferredLanguageCode,
                                        null,
                                        out _localChatControlHandle
                                    ));

                    PartySucceeded(SDK.PartyChatControlSetAudioInputMuted(_localChatControlHandle, LocalPlayer.IsMuted));
                    PartySucceeded(SDK.PartyChatControlSetAudioRenderVolume(
                            _localChatControlHandle,
                            _localChatControlHandle,
                            LocalPlayer.VoiceLevel
                            ));
                }

                if (_localChatControlHandle != null)
                {
                    // update a reference to local chat control in the local player object
                    _localPlayer._chatControlHandle = _localChatControlHandle;
                }
                _SetPlayFabMultiplayerManagerInternalState(_InternalPlayFabMultiplayerManagerState.LocalUserCreated);
                if (_queuedStartCreateAndJoinNetworkCreateLocalUserOp.queued)
                {
                    _queuedStartCreateAndJoinNetworkCreateLocalUserOp.queued = false;
                    CreateAndJoinNetworkImplStart(_queuedStartCreateAndJoinNetworkCreateLocalUserOp.networkConfiguration);
                }
                if (_queuedJoinNetworkCreateLocalUserOp.queued)
                {
                    _queuedJoinNetworkCreateLocalUserOp.queued = false;
                    JoinNetworkImplStart(_queuedJoinNetworkCreateLocalUserOp.networkId);
                }
            }
        }

        private void GetEntityTokenFailed(PlayFabError error)
        {
            _LogError(error.ErrorMessage);
        }

        private void CreateAndJoinNetworkImplStart(PlayFabNetworkConfiguration networkConfiguration)
        {
            _LogInfo("PlayFabMultiplayerManager:CreateAndJoinNetworkImplStart()");

            if (networkConfiguration == null)
            {
                _LogError(_ErrorMessageMissingNetworkConfiguration);
                return;
            }
            if (_platformPolicyProvider == null &&
                !PlayFabAuthenticationAPI.IsEntityLoggedIn())
            {
                _LogError(_ErrorMessageNoUserLoggedIn);
                return;
            }

            // If we get called when we already have a queued join network operation, join with the new information.
            if (_isJoinNetworkInProgress)
            {
                _queuedStartCreateAndJoinNetworkCreateLocalUserOp.networkConfiguration = networkConfiguration;
            }

            // Need to queue the operation in case we're not ready yet.
            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.LocalUserCreated)
            {
                _LogInfo("PlayFabMultiplayerManager:CreateAndJoinNetworkImplStart():QueueStartCreateAndJoinNetworkCreateLocalUserOp");

                _queuedStartCreateAndJoinNetworkCreateLocalUserOp = new QueuedStartCreateAndJoinNetworkOp()
                {
                    queued = true,
                    networkConfiguration = networkConfiguration
                };
                _isJoinNetworkInProgress = true;
                return;
            }

            // If we are already in a network, leave the network
            if (_playFabMultiplayerManagerState >= _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
            {
                _LogInfo("PlayFabMultiplayerManager:CreateAndJoinNetworkImplStart():QueuedCreateAndJoinAfterLeaveNetworkOp");

                _queuedCreateAndJoinAfterLeaveNetworkOp = new QueuedCreateAndJoinAfterLeaveNetworkOp()
                {
                    queued = true,
                    networkConfiguration = networkConfiguration
                };
                LeaveNetworkImpl(false);
            }
            else
            {
                CreateAndJoinNetworkImplComplete(networkConfiguration);
            }
        }

        private void CreateAndJoinNetworkImplComplete(PlayFabNetworkConfiguration networkConfiguration)
        {
            _LogInfo("PlayFabMultiplayerManager:CreateAndJoinNetworkImplComplete()");

            var partyNetworkConfiguration = new PARTY_NETWORK_CONFIGURATION
            {
                MaxDeviceCount = (uint)networkConfiguration.MaxPlayerCount,
                MaxDevicesPerUserCount = _DEVICES_PER_USER_COUNT,
                MaxEndpointsPerDeviceCount = _ENDPOINTS_PER_DEVICE_COUNT,
                MaxUserCount = (uint)networkConfiguration.MaxPlayerCount,
                MaxUsersPerDeviceCount = _USERS_PER_DEVICE,
                DirectPeerConnectivityOptions = networkConfiguration.DirectPeerConnectivityOptions
            };

            var partyInvitationConfiguration = new PARTY_INVITATION_CONFIGURATION
            {
                Identifier = Guid.NewGuid().ToString(),
                Revocability = PARTY_INVITATION_REVOCABILITY.PARTY_INVITATION_REVOCABILITY_ANYONE,
                EntityIds = null
            };
            PARTY_REGION[] regions = { };

            _generatedInvitationId = string.Empty;
            PartySucceeded(SDK.PartyCreateNewNetwork(
                _partyHandle,
                _localUserHandle,
                partyNetworkConfiguration,
                regions,
                partyInvitationConfiguration,
                null,
                out _networkDescriptor,
                out _generatedInvitationId));

            PartySucceeded(SDK.PartyConnectToNetwork(
                    _partyHandle,
                   _networkDescriptor,
                   null,
                   out _networkHandle
                ));
        }

        private void JoinNetworkImplStart(string networkId)
        {
            _LogInfo("PlayFabMultiplayerManager:JoinNetworkImplStart");

            if (string.IsNullOrEmpty(networkId))
            {
                _LogError(_ErrorMessageMissingNetworkId);
                return;
            }
            if (_platformPolicyProvider == null &&
                !PlayFabAuthenticationAPI.IsEntityLoggedIn())
            {
                _LogError(_ErrorMessageNoUserLoggedIn);
                return;
            }

            // If there is already a join network operation queued, update the operation with the new network ID.
            if (_isJoinNetworkInProgress)
            {
                _queuedJoinNetworkCreateLocalUserOp.networkId = networkId;
            }

            // Need to queue the operation in case we're not ready yet.
            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.LocalUserCreated)
            {
                _LogInfo("PlayFabMultiplayerManager:JoinNetworkImplStart:QueueJoinNetworkCreateLocalUserOp");

                _queuedJoinNetworkCreateLocalUserOp = new QueuedJoinNetworkOp()
                {
                    queued = true,
                    networkId = networkId,
                };
                _isJoinNetworkInProgress = true;
                return;
            }

            // If we are already in a network, leave the network
            if (_playFabMultiplayerManagerState >= _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
            {
                _LogInfo("PlayFabMultiplayerManager:JoinNetworkImplStart:QueuedCompleteJoinAfterLeaveNetworkOp");

                _queuedCompleteJoinAfterLeaveNetworkOp = new QueuedCompleteJoinAfterLeaveNetworkOp()
                {
                    queued = true,
                    networkId = networkId
                };
                LeaveNetworkImpl(false);
            }
            else
            {
                JoinNetworkImplComplete(networkId);
            }
        }

        private void JoinNetworkImplComplete(string networkId)
        {
            _LogInfo("PlayFabMultiplayerManager:JoinNetworkImplComplete");

            _networkDescriptor = GetNetworkDescriptorFromNetworkId(networkId);
            if (_networkDescriptor != null)
            {
                PartySucceeded(SDK.PartyConnectToNetwork(
                    _partyHandle,
                    _networkDescriptor,
                    null,
                    out _networkHandle
                    ));
            }
            else
            {
                _LogError("Network ID is not the correct format.");
            }
        }

        private void LeaveNetworkImpl(bool wasCallInitiatedByDeveloper)
        {
            _LogInfo("PlayFabMultiplayerManager:LeaveNetworkImpl, wasCallInitiatedByDeveloper: " + wasCallInitiatedByDeveloper);

            if (wasCallInitiatedByDeveloper)
            {
                _queuedCreateAndJoinAfterLeaveNetworkOp.queued = false;
                _queuedCompleteJoinAfterLeaveNetworkOp.queued = false;
            }

            if (_isLeaveNetworkInProgress ||
                _networkHandle == null)
            {
                return;
            }

            uint errorCode = SDK.PartyNetworkLeaveNetwork(
                                    _networkHandle,
                                    null
                                    );
            if (PartyError.FAILED(errorCode))
            {
                if (errorCode == _c_ErrorObjectIsBeingDestroyed)
                {
                    _LogInfo("Client is trying to leave a network that does not exist anymore.");
                }
                else
                {
                    _LogError(errorCode);
                }
            }
            else
            {
                _cachedAllChatHandlesList = null;
                _isLeaveNetworkInProgress = true;
                _networkDescriptor = null;
                _networkHandle = null;
            }
        }

        // The Network ID is an opaque string that includes both the invitation ID and the network descriptor.
        private void UpdateNetworkId(string invitationId, PARTY_NETWORK_DESCRIPTOR networkDescriptor)
        {
            _LogInfo("PlayFabMultiplayerManager:UpdateNetworkId()");

            _networkDescriptor = networkDescriptor;
            string serializedNetworkDescriptor = string.Empty;
            PartySucceeded(SDK.PartySerializeNetworkDescriptor(_networkDescriptor, out serializedNetworkDescriptor));
            _networkId = invitationId + _NETWORK_ID_INVITE_AND_DESCRIPTOR_SEPERATOR + serializedNetworkDescriptor;
        }

        private void ResetNetworkManagerStateAfterFailureToConnect()
        {
            _LogInfo("PlayFabMultiplayerManager:ResetNetworkManagerStateAfterFailureToConnect()");

            _networkHandle = null;
            _networkDescriptor = null;
            _generatedInvitationId = null;
        }

        private void AuthenticateLocalUserStart()
        {
            _LogInfo("PlayFabMultiplayerManager:AuthenticateLocalUserStart()");

            PartySucceeded(SDK.PartyNetworkAuthenticateLocalUser(
                _networkHandle,
                _localUserHandle,
                _generatedInvitationId,
                null
                ));

            PartySucceeded(SDK.PartyNetworkCreateEndpoint(
                _networkHandle,
                _localUserHandle,
                null,
                null,
                out _localEndPointHandle
                ));

            PartySucceeded(SDK.PartyNetworkConnectChatControl(
                _networkHandle,
                _localChatControlHandle,
                null
                ));
        }

        private void AuthenticateLocalUserComplete()
        {
            _LogInfo("PlayFabMultiplayerManager:AuthenticateLocalUserComplete()");

            _playFabMultiplayerManagerState = _InternalPlayFabMultiplayerManagerState.LocalUserAuthenticated;
            SetUserSettings();

            // We fire OnNetworkJoined here rather than earlier because the developer will want to have values populated for objects
            // like the LocalUser, which won't be populated right after the network gets created.
            _isJoinNetworkInProgress = false;
            if (OnNetworkJoined != null)
            {
                _playFabMultiplayerManagerState = _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork;
                OnNetworkJoined(this, _networkId);
            }

            // apply transcription options
            SpeechToTextMode = SpeechToTextMode;

            // apply chat translation options
            TranslateChat = TranslateChat;
        }

        private void SetUserSettings()
        {
            _LogInfo("PlayFabMultiplayerManager:SetUserSettings()");

#if (UNITY_GAMECORE || MICROSOFT_GAME_CORE) && !UNITY_EDITOR
            if (string.IsNullOrEmpty(LocalPlayer.PlatformSpecificUserId))
            {
                _platformPolicyProvider.CreateOrUpdatePlatformUser(LocalPlayer, true);
            }
            PartySucceeded(SDK.PartyChatControlSetAudioInput(
                    _localChatControlHandle,
                    PARTY_AUDIO_DEVICE_SELECTION_TYPE.PARTY_AUDIO_DEVICE_SELECTION_TYPE_PLATFORM_USER_DEFAULT,
                    LocalPlayer.PlatformSpecificUserId,
                    null
                ));
            PartySucceeded(SDK.PartyChatControlSetAudioOutput(
                    _localChatControlHandle,
                    PARTY_AUDIO_DEVICE_SELECTION_TYPE.PARTY_AUDIO_DEVICE_SELECTION_TYPE_PLATFORM_USER_DEFAULT,
                    LocalPlayer.PlatformSpecificUserId,
                    null
                    ));
#elif UNITY_PS4 || UNITY_PS5
            int userId = GetUserId(); // Implementation is provided by PlayFab Party Unity plugin for PS4
            PartySucceeded(SDK.PartyChatControlSetAudioInput(
                    _localChatControlHandle,
                    PARTY_AUDIO_DEVICE_SELECTION_TYPE.PARTY_AUDIO_DEVICE_SELECTION_TYPE_PLATFORM_USER_DEFAULT,
                    userId.ToString(),
                    userId.ToString()
                ));
            PartySucceeded(SDK.PartyChatControlSetAudioOutput(
                    _localChatControlHandle,
                    PARTY_AUDIO_DEVICE_SELECTION_TYPE.PARTY_AUDIO_DEVICE_SELECTION_TYPE_PLATFORM_USER_DEFAULT,
                    userId.ToString(),
                    userId.ToString()
                    ));
#else
            PartySucceeded(SDK.PartyChatControlSetAudioInput(
                    _localChatControlHandle,
                    PARTY_AUDIO_DEVICE_SELECTION_TYPE.PARTY_AUDIO_DEVICE_SELECTION_TYPE_SYSTEM_DEFAULT,
                    string.Empty,
                    null
                ));
            PartySucceeded(SDK.PartyChatControlSetAudioOutput(
                    _localChatControlHandle,
                    PARTY_AUDIO_DEVICE_SELECTION_TYPE.PARTY_AUDIO_DEVICE_SELECTION_TYPE_SYSTEM_DEFAULT,
                    string.Empty,
                    null
                    ));
#endif
            PartySucceeded(SDK.PartyChatControlPopulateAvailableTextToSpeechProfiles(
                    _localChatControlHandle,
                    null
                    ));
        }

        private bool IsTextToSpeechEnabled()
        {
            if (TextToSpeechMode == AccessibilityMode.None)
            {
                return false;
            }
            else if (TextToSpeechMode == AccessibilityMode.Enabled)
            {
                return true;
            }
            else
            {
                // TextToSpeechMode is AccessibilityMode.PlatformDefault
                if (_platformPolicyProvider != null)
                {
                    return _platformPolicyProvider.IsTextToSpeechEnabled();
                }
                else
                {
                    return false;
                }
            }
        }

        private void SetTextChatOptions(PARTY_TEXT_CHAT_OPTIONS textChatOptions)
        {
            PartySucceeded(SDK.PartyChatControlSetTextChatOptions(_localChatControlHandle, textChatOptions, null));
        }

        private void SetTranscriptionOptions(PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS transcriptionOptions)
        {
            PartySucceeded(SDK.PartyChatControlSetTranscriptionOptions(_localChatControlHandle, transcriptionOptions, null));
        }

        internal void _SendDataMessageToAllPlayers(byte[] buffer)
        {
            _LogInfo("PlayFabMultiplayerManager:_SendDataMessageToAllPlayers(byte[] buffer)");

            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
            {
                _LogError(_ErrorMessageCannotCallAPINotConnectedToNetwork);
                return;
            }
            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.LocalUserAuthenticated)
            {
                return;
            }
            if (buffer.Count<byte>() == 0)
            {
                _LogError(_ErrorMessageEmptyDataMessagePayload);
                return;
            }

            PartySucceeded(SDK.PartyEndpointSendMessage(
                    _localEndPointHandle,
                    null,
                    _defaultSendOptions,
                    _defaultQueuingConfiguration,
                    buffer
                ));
        }

        internal bool _SendDataMessage(byte[] buffer, IEnumerable<PlayFabPlayer> recipients, DeliveryOption deliveryOption)
        {
            _LogInfo("PlayFabMultiplayerManager:_SendDataMessage(byte[] buffer, IEnumerable<PlayFabPlayer> recipients, DeliveryOption deliveryOption)");

            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
            {
                _LogError(_ErrorMessageCannotCallAPINotConnectedToNetwork);
                return false;
            }
            if (buffer.Count() == 0)
            {
                _LogError(_ErrorMessageEmptyDataMessagePayload);
                return false;
            }
            if (recipients.Count() > PartyConstants.c_maxNetworkConfigurationMaxDeviceCount)
            {
                _LogError(_ErrorMessageTooManyRecipients);
                return false;
            }

            PARTY_ENDPOINT_HANDLE[] targetEndpoints = EndPointHandlesFromPlayFabPlayerListNoGC(recipients);
            PARTY_SEND_MESSAGE_OPTIONS sendOptions = SendOptionsFromDeliveryOption(deliveryOption);
            return PartySucceeded(SDK.PartyEndpointSendMessage(
                    _localEndPointHandle,
                    targetEndpoints,
                    sendOptions,
                    _defaultQueuingConfiguration,
                    buffer
                ));
        }

        internal void _SendDataMessage(IntPtr buffer, uint bufferSize, IEnumerable<PlayFabPlayer> recipients, DeliveryOption deliveryOption)
        {
            _LogInfo("PlayFabMultiplayerManager:_SendDataMessage(IntPtr buffer, uint bufferSize, IEnumerable<PlayFabPlayer> recipients, DeliveryOption deliveryOption)");

            if (bufferSize == 0)
            {
                _LogError(_ErrorMessageEmptyDataMessagePayload);
                return;
            }

            PARTY_ENDPOINT_HANDLE[] targetEndpoints = EndPointHandlesFromPlayFabPlayerListNoGC(recipients);
            PARTY_SEND_MESSAGE_OPTIONS sendOptions = SendOptionsFromDeliveryOption(deliveryOption);
            PartySucceeded(SDK.PartyEndpointSendMessage(
                    _localEndPointHandle,
                    targetEndpoints,
                    sendOptions,
                    _defaultQueuingConfiguration,
                    buffer,
                    bufferSize
                ));
        }

        internal void _SendChatMessageToAllPlayers(string message)
        {
            _LogInfo("PlayFabMultiplayerManager:_SendChatMessageToAllPlayers()");

            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
            {
                _LogError(_ErrorMessageCannotCallAPINotConnectedToNetwork);
                return;
            }
            if (_cachedAllChatHandlesList == null)
            {
                return;
            }

            _SendChatMessageImpl(message, _cachedAllChatHandlesList);
        }

        internal void _SendChatMessage(string message, IEnumerable<PlayFabPlayer> recipients)
        {
            _LogInfo("PlayFabMultiplayerManager:_SendChatMessage()");

            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
            {
                _LogError(_ErrorMessageCannotCallAPINotConnectedToNetwork);
                return;
            }
            if (recipients == null ||
                recipients.Count<PlayFabPlayer>() == 0)
            {
                _LogWarning("Warning: No recipients specified.");
                return;
            }
            if (recipients.Count() > PartyConstants.c_maxNetworkConfigurationMaxDeviceCount)
            {
                _LogError(_ErrorMessageTooManyRecipients);
                return;
            }

            PARTY_CHAT_CONTROL_HANDLE[] targetChatControlHandles = ChatControlHandlesFromPlayFabPlayerListNoGC(recipients);
            _SendChatMessageImpl(message, targetChatControlHandles);
        }

        private void _SendChatMessageImpl(string message, PARTY_CHAT_CONTROL_HANDLE[] targetChatControlHandles)
        {
            _LogInfo("PlayFabMultiplayerManager:_SendChatMessageImpl()");

            // synthesize speech for outgoing chat message if text-to-speech is enabled
            if (IsTextToSpeechEnabled())
            {
                PartySucceeded(SDK.PartyChatControlSynthesizeTextToSpeech(
                        _localChatControlHandle,
                        PARTY_SYNTHESIZE_TEXT_TO_SPEECH_TYPE.PARTY_SYNTHESIZE_TEXT_TO_SPEECH_TYPE_VOICE_CHAT,
                        message,
                        null));
            }

            PartySucceeded(SDK.PartyChatControlSendText(
                _localChatControlHandle,
                targetChatControlHandles,
                message,
                null
                ));
        }

        private PARTY_SEND_MESSAGE_OPTIONS SendOptionsFromDeliveryOption(DeliveryOption deliveryOption)
        {
            PARTY_SEND_MESSAGE_OPTIONS sendOptions =
             PARTY_SEND_MESSAGE_OPTIONS.PARTY_SEND_MESSAGE_OPTIONS_SEQUENTIAL_DELIVERY |
             PARTY_SEND_MESSAGE_OPTIONS.PARTY_SEND_MESSAGE_OPTIONS_COALESCE_OPPORTUNISTICALLY;
            if (deliveryOption == DeliveryOption.BestEffort)
            {
                sendOptions |= PARTY_SEND_MESSAGE_OPTIONS.PARTY_SEND_MESSAGE_OPTIONS_BEST_EFFORT_DELIVERY;
            }
            else
            {
                sendOptions |= PARTY_SEND_MESSAGE_OPTIONS.PARTY_SEND_MESSAGE_OPTIONS_GUARANTEED_DELIVERY;
            }
            return sendOptions;
        }

        private void UpdateCachedChatControlsList()
        {
            List<PARTY_CHAT_CONTROL_HANDLE> targetChatControlHandles = new List<PARTY_CHAT_CONTROL_HANDLE>();
            foreach (var remotePlayer in _remotePlayers)
            {
                targetChatControlHandles.Add(remotePlayer._chatControlHandle);
            }
            _cachedAllChatHandlesList = targetChatControlHandles.ToArray();
        }

        internal void _SetMuted(EntityKey entityKey, bool isMuted, bool isLocal)
        {
            _LogInfo("PlayFabMultiplayerManager:_SetMuted()");

            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.LocalUserCreated)
            {
                // If the local player has not been created yet, we will perform the muted operation when the chat control
                // is created so we just return. We don't have to worry about remote players, because they won't be
                // available until the client is connected to the network.
                return;
            }

            if (isLocal == true)
            {
                PartySucceeded(SDK.PartyChatControlSetAudioInputMuted(_localChatControlHandle, isMuted));
            }
            else
            {
                PlayFabPlayer remotePlayer = GetPlayerByEntityId(entityKey.Id);
                if (remotePlayer != null)
                {
                    PartySucceeded(SDK.PartyChatControlSetIncomingAudioMuted(
                        _localChatControlHandle,
                        remotePlayer._chatControlHandle,
                        isMuted
                        ));

                    PARTY_CHAT_PERMISSION_OPTIONS chatPermissions = _CHAT_PERMISSIONS_ALL;
                    if (isMuted)
                    {
                        chatPermissions = PARTY_CHAT_PERMISSION_OPTIONS.PARTY_CHAT_PERMISSION_OPTIONS_NONE;
                        PartySucceeded(SDK.PartyChatControlSetPermissions(
                                            _localChatControlHandle,
                                            remotePlayer._chatControlHandle,
                                            chatPermissions
                                        ));
                    }
                    else
                    {
                        if (_platformPolicyProvider != null)
                        {
                            chatPermissions = _platformPolicyProvider.GetChatPermissions(remotePlayer);
                        }
                        else
                        {
                            chatPermissions = _CHAT_PERMISSIONS_ALL;
                        }
                        PartySucceeded(SDK.PartyChatControlSetPermissions(
                                            _localChatControlHandle,
                                            remotePlayer._chatControlHandle,
                                            chatPermissions
                                        ));
                    }
                }
                else
                {
                    _LogError(_ErrorMessagePlayerNotFound);
                }
            }
        }

        internal void _RaiseDataMessageReceivedEvent(PlayFabPlayer fromPlayer, IntPtr buffer, uint bufferSize)
        {
            _LogInfo("PlayFabMultiplayerManager:_RaiseDataMessageReceivedEvent()");

            if (OnDataMessageReceived != null)
            {
                byte[] bufferAsBytes = new byte[bufferSize];
                if (bufferSize > 0)
                {
                    Marshal.Copy(buffer, bufferAsBytes, 0, (int)bufferSize);
                }
                OnDataMessageReceived(this, fromPlayer, bufferAsBytes);
            }
            if (OnDataMessageNoCopyReceived != null)
            {
                OnDataMessageNoCopyReceived(this, fromPlayer, buffer, bufferSize);
            }
        }

        internal void _RaiseChatMessageReceivedEvent(PlayFabPlayer fromPlayer, string message, ChatMessageType chatMessageType)
        {
            _LogInfo("PlayFabMultiplayerManager:_RaiseChatMessageReceivedEvent()");

            if (OnChatMessageReceived != null)
            {
                OnChatMessageReceived(this, fromPlayer, message, chatMessageType);
            }
        }

        internal bool _IsOnDataMessageSubscribedTo()
        {
            return OnDataMessageReceived != null;
        }

        internal string _GetPlatformSpecificUserId(EntityKey entityKey)
        {
            string platformSpecificUserId = string.Empty;
            if (entityKey != null)
            {
                PlayFabPlayer player = GetPlayerByEntityId(entityKey.Id);
                if (player != null)
                {
                    platformSpecificUserId = player._platformSpecificUserId;
                }
            }
            return platformSpecificUserId;
        }

        internal ChatState _GetChatState(EntityKey entityKey, bool _isLocal)
        {
            ChatState chatState = ChatState.Silent;
            PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR localChatControlIndicator;
            if (_isLocal)
            {
                if (_localChatControlHandle != null)
                {
                    SDK.PartyChatControlGetLocalChatIndicator(
                        _localChatControlHandle,
                        out localChatControlIndicator
                    );
                    switch (localChatControlIndicator)
                    {
                        case PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR.PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR_NO_AUDIO_INPUT:
                            chatState = ChatState.NoAudioInput;
                            break;
                        case PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR.PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR_AUDIO_INPUT_MUTED:
                            chatState = ChatState.Muted;
                            break;
                        case PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR.PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR_SILENT:
                            chatState = ChatState.Silent;
                            break;
                        case PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR.PARTY_LOCAL_CHAT_CONTROL_CHAT_INDICATOR_TALKING:
                            chatState = ChatState.Talking;
                            break;
                        default:
                            chatState = ChatState.Silent;
                            break;
                    }
                }
            }
            else
            {
                PARTY_CHAT_CONTROL_CHAT_INDICATOR chatControlIndicator;
                PARTY_CHAT_CONTROL_HANDLE targetChatControlHandle = null;
                PlayFabPlayer player = null;
                player = GetPlayerByEntityId(entityKey.Id);
                if (player != null)
                {
                    targetChatControlHandle = player._chatControlHandle;
                }
                if (_localChatControlHandle != null &&
                    targetChatControlHandle != null)
                {
                    PartySucceeded(SDK.PartyChatControlGetChatIndicator(
                    _localChatControlHandle,
                    targetChatControlHandle,
                    out chatControlIndicator
                    ));

                    switch (chatControlIndicator)
                    {
                        case PARTY_CHAT_CONTROL_CHAT_INDICATOR.PARTY_CHAT_CONTROL_CHAT_INDICATOR_INCOMING_COMMUNICATIONS_MUTED:
                            if (player != null &&
                                player._mutedByPlatform)
                            {
                                chatState = ChatState.MutedByPlatform;
                            }
                            else
                            {
                                chatState = ChatState.Muted;
                            }
                            break;
                        case PARTY_CHAT_CONTROL_CHAT_INDICATOR.PARTY_CHAT_CONTROL_CHAT_INDICATOR_INCOMING_VOICE_DISABLED:
                            chatState = ChatState.Muted;
                            break;
                        case PARTY_CHAT_CONTROL_CHAT_INDICATOR.PARTY_CHAT_CONTROL_CHAT_INDICATOR_SILENT:
                            chatState = ChatState.Silent;
                            break;
                        case PARTY_CHAT_CONTROL_CHAT_INDICATOR.PARTY_CHAT_CONTROL_CHAT_INDICATOR_TALKING:
                            chatState = ChatState.Talking;
                            break;
                        default:
                            chatState = ChatState.Silent;
                            break;
                    }
                }
                else
                {
                    chatState = ChatState.NoAudioInput;
                }
            }

            return chatState;
        }

        internal float _GetVoiceLevel(EntityKey entityKey)
        {
            float voiceLevel = 0f;
            PlayFabPlayer player = GetPlayerByEntityId(entityKey.Id);
            if (player != null)
            {
                PartySucceeded(SDK.PartyChatControlGetAudioRenderVolume(
                    _localChatControlHandle,
                    player._chatControlHandle,
                    out voiceLevel
                    ));
            }
            return voiceLevel;
        }

        internal void _SetVoiceLevel(EntityKey entityKey, float voiceLevel, bool _isLocal)
        {
            _LogInfo("PlayFabMultiplayerManager:_SetVoiceLevel()");

            if (_playFabMultiplayerManagerState < _InternalPlayFabMultiplayerManagerState.LocalUserCreated)
            {
                // We can return early if the local user hasn't been created. The volume level will still be
                // set. The reason is that we set volume level when the local user is created. Remote users
                // won't be available anyway until the client is connected to the network.
                return;
            }
            else
            {
                PARTY_CHAT_CONTROL_HANDLE targetHandle = null;
                if (_isLocal)
                {
                    targetHandle = _localChatControlHandle;
                }
                else
                {
                    PlayFabPlayer player = GetPlayerByEntityId(entityKey.Id);
                    if (player != null)
                    {
                        targetHandle = player._chatControlHandle;
                    }
                }
                PartySucceeded(SDK.PartyChatControlSetAudioRenderVolume(
                        _localChatControlHandle,
                        targetHandle,
                        voiceLevel
                        ));
            }
        }

        internal string _GetLanguageCode(EntityKey entityKey, bool isLocal)
        {
            string languageCode = string.Empty;
            if (_playFabMultiplayerManagerState >= _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork)
            {
                if (isLocal)
                {
                    PartySucceeded(SDK.PartyChatControlGetLanguage(
                        _localChatControlHandle,
                        out languageCode
                        ));
                }
                else
                {
                    PlayFabPlayer player = GetPlayerByEntityId(entityKey.Id);
                    if (player != null)
                    {
                        PartySucceeded(SDK.PartyChatControlGetLanguage(
                            player._chatControlHandle,
                            out languageCode
                            ));
                    }
                }
            }
            return languageCode;
        }

        internal void _SetPlayFabMultiplayerManagerInternalState(_InternalPlayFabMultiplayerManagerState state)
        {
            _playFabMultiplayerManagerState = state;
        }

        // Private methods
        private void SetRemotePlayerChatControlHandle(string entityId, PARTY_CHAT_CONTROL_HANDLE remoteChatControlHandle)
        {
            _LogInfo("PlayFabMultiplayerManager:SetRemotePlayerChatControlHandle()");

            // Find the player and set their remoteChatHandle
            foreach (var playerKeyValuePair in _remotePlayers)
            {
                if (playerKeyValuePair.EntityKey.Id == entityId)
                {
                    playerKeyValuePair._chatControlHandle = remoteChatControlHandle;
                    break;
                }
            }
        }

        internal bool PartySucceeded(uint errorCode)
        {
            bool succeeded = false;
            if (PartyError.FAILED(errorCode))
            {
                _LogError(errorCode);
            }
            else
            {
                succeeded = true;
            }

            return succeeded;
        }

        internal bool PartySucceeded(uint errorCode, PlayFabMultiplayerManagerErrorType errorType)
        {
            bool succeeded = false;
            if (PartyError.FAILED(errorCode))
            {
                _LogError(errorCode, errorType);
            }
            else
            {
                succeeded = true;
            }

            return succeeded;
        }

        internal bool InternalCheckStateChangeSucceededOrLogErrorIfFailed(PARTY_STATE_CHANGE_RESULT result, uint errorCode)
        {
            bool succeeded = false;
            if (result == PARTY_STATE_CHANGE_RESULT.PARTY_STATE_CHANGE_RESULT_SUCCEEDED)
            {
                succeeded = true;
            }
            else if (result == PARTY_STATE_CHANGE_RESULT.PARTY_STATE_CHANGE_RESULT_LEAVE_NETWORK_CALLED)
            {
                succeeded = true;
            }
            else
            {
                InternalCheckStateChangeSucceededOrLogErrorIfFailedImpl(result.ToString(), errorCode);
            }
            return succeeded;
        }

        internal bool InternalCheckStateChangeSucceededOrLogErrorIfFailed(PARTY_XBL_STATE_CHANGE_RESULT result, uint errorCode)
        {
            bool succeeded = false;
            if (result == PARTY_XBL_STATE_CHANGE_RESULT.PARTY_XBL_STATE_CHANGE_RESULT_SUCCEEDED)
            {
                succeeded = true;
            }
            else
            {
                InternalCheckStateChangeSucceededOrLogErrorIfFailedImpl(result.ToString(), errorCode);
            }
            return succeeded;
        }

        private void InternalCheckStateChangeSucceededOrLogErrorIfFailedImpl(string stateChangeString, uint errorCode)
        {
            _LogError(stateChangeString);
            PartySucceeded(errorCode);
        }

        private bool RaiseErrorIfStateChangedFailed(PARTY_STATE_CHANGE_RESULT result, uint errorCode)
        {
            bool succeeded = false;
            if (result == PARTY_STATE_CHANGE_RESULT.PARTY_STATE_CHANGE_RESULT_SUCCEEDED)
            {
                succeeded = true;
            }
            else
            {
                PartySucceeded(errorCode);
            }
            return succeeded;
        }

        private void ProcessStateChanges()
        {
            if (_playFabMultiplayerManagerState >= _InternalPlayFabMultiplayerManagerState.LocalUserCreated &&
                _playFabMultiplayerManagerState != _InternalPlayFabMultiplayerManagerState.NotInitialized)
            {
                if (_partyStateChanges == null)
                {
                    _partyStateChanges = new List<PARTY_STATE_CHANGE>();
                }
                if (PartySucceeded(SDK.PartyStartProcessingStateChanges(_partyHandle, out _partyStateChanges)))
                {
                    foreach (PARTY_STATE_CHANGE stateChange in _partyStateChanges)
                    {
                        _LogInfo("Party State change: " + stateChange.StateChangeType.ToString());
                        switch (stateChange.StateChangeType)
                        {
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_REGIONS_CHANGED:
                                {
                                    var stateChangeConverted = (PARTY_REGIONS_CHANGED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_AUTHENTICATE_LOCAL_USER_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_AUTHENTICATE_LOCAL_USER_COMPLETED_STATE_CHANGE)stateChange;
                                    if (InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail))
                                    {
                                        AuthenticateLocalUserComplete();
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_DESTROY_LOCAL_USER_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_DESTROY_LOCAL_USER_COMPLETED_STATE_CHANGE)stateChange;
                                    RaiseErrorIfStateChangedFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_LOCAL_USER_REMOVED:
                                {
                                    // No-op. We raise the left network event and process other logic on the state change for leaving the network.
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CREATE_CHAT_CONTROL_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_CREATE_CHAT_CONTROL_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CREATE_ENDPOINT_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_CREATE_ENDPOINT_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CREATE_INVITATION_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_CREATE_INVITATION_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CREATE_NEW_NETWORK_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_CREATE_NEW_NETWORK_COMPLETED_STATE_CHANGE)stateChange;
                                    if (!InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail))
                                    {
                                        ResetNetworkManagerStateAfterFailureToConnect();
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_DATA_BUFFERS_RETURNED:
                                {
                                    // No-op. This functionality is not implemented in the SDK.
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_DESTROY_CHAT_CONTROL_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_DESTROY_CHAT_CONTROL_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_DESTROY_ENDPOINT_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_DESTROY_ENDPOINT_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_DISCONNECT_CHAT_CONTROL_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_DISCONNECT_CHAT_CONTROL_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_LEAVE_NETWORK_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_LEAVE_NETWORK_COMPLETED_STATE_CHANGE)stateChange;
                                    if (InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail))
                                    {
                                        _isLeaveNetworkInProgress = false;
                                        if (OnNetworkLeft != null)
                                        {
                                            OnNetworkLeft(this, _networkId);
                                            _networkId = null;
                                        }
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_LOCAL_CHAT_AUDIO_INPUT_CHANGED:
                                {
                                    var stateChangeConverted = (PARTY_LOCAL_CHAT_AUDIO_INPUT_CHANGED_STATE_CHANGE)stateChange;
                                    uint errorCode = stateChangeConverted.errorDetail;
                                    if (!PartySucceeded(errorCode))
                                    {
                                        if (errorCode == _c_ErrorFailedToFindResourceSpecified)
                                        {
                                            _LogWarning("No audio input device found.");
                                        }
                                    }
                                }
                                break;
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_LOCAL_CHAT_AUDIO_OUTPUT_CHANGED:
                                {
                                    var stateChangeConverted = (PARTY_LOCAL_CHAT_AUDIO_OUTPUT_CHANGED_STATE_CHANGE)stateChange;
                                    uint errorCode = stateChangeConverted.errorDetail;
                                    if (!PartySucceeded(errorCode))
                                    {
                                        if (errorCode == _c_ErrorFailedToFindResourceSpecified)
                                        {
                                            _LogWarning("No audio output device found.");
                                        }
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_NETWORK_CONFIGURATION_MADE_AVAILABLE:
                                {
                                    // No-op.
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_NETWORK_DESTROYED:
                                {
                                    var stateChangeConverted = (PARTY_NETWORK_DESTROYED_STATE_CHANGE)stateChange;
                                    if (PartySucceeded(stateChangeConverted.errorDetail))
                                    {
                                        if (_queuedCreateAndJoinAfterLeaveNetworkOp.queued ||
                                            _queuedCompleteJoinAfterLeaveNetworkOp.queued)
                                        {
                                            if (_queuedCreateAndJoinAfterLeaveNetworkOp.queued)
                                            {
                                                _queuedCreateAndJoinAfterLeaveNetworkOp.queued = false;
                                                CreateAndJoinNetworkImplComplete(_queuedCreateAndJoinAfterLeaveNetworkOp.networkConfiguration);
                                            }
                                            if (_queuedCompleteJoinAfterLeaveNetworkOp.queued)
                                            {
                                                _queuedCompleteJoinAfterLeaveNetworkOp.queued = false;
                                                JoinNetworkImplComplete(_queuedCompleteJoinAfterLeaveNetworkOp.networkId);
                                            }
                                        }
                                    }
                                    _playFabMultiplayerManagerState = _InternalPlayFabMultiplayerManagerState.Initialized;
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_REMOTE_DEVICE_CREATED:
                                {
                                    // No-op.
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_REMOTE_DEVICE_DESTROYED:
                                {
                                    // No-op.
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_REMOVE_LOCAL_USER_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_REMOVE_LOCAL_USER_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_REVOKE_INVITATION_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_REVOKE_INVITATION_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_SET_TEXT_CHAT_OPTIONS_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_SET_TEXT_CHAT_OPTIONS_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_SET_TEXT_TO_SPEECH_PROFILE_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_SET_TEXT_TO_SPEECH_PROFILE_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_SET_TRANSCRIPTION_OPTIONS_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_SET_TRANSCRIPTION_OPTIONS_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_SYNTHESIZE_TEXT_TO_SPEECH_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_SYNTHESIZE_TEXT_TO_SPEECH_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_ENDPOINT_CREATED:
                                {
                                    var stateChangeConverted = (PARTY_ENDPOINT_CREATED_STATE_CHANGE)stateChange;
                                    PartySucceeded(SDK.PartyNetworkGetNetworkDescriptor(
                                        stateChangeConverted.network,
                                        out _networkDescriptor
                                        ));
                                    var newEndpoint = stateChangeConverted.endpoint;
                                    string newPlayerEntityId = string.Empty;
                                    PartySucceeded(SDK.PartyEndpointGetEntityId(newEndpoint, out newPlayerEntityId));

                                    bool isLocal = false;
                                    PartySucceeded(SDK.PartyEndpointIsLocal(newEndpoint, out isLocal));
                                    if (!isLocal)
                                    {
                                        PlayFabPlayer newPlayer = GetPlayerByEntityId(newPlayerEntityId) as PlayFabPlayer;
                                        if (newPlayer == null)
                                        {
                                            newPlayer = new PlayFabPlayer();
                                            newPlayer._endPointHandle = newEndpoint;
                                            newPlayer._isLocal = isLocal;
                                            EntityKey newEntityKey = new EntityKey();
                                            newEntityKey.Id = newPlayerEntityId;
                                            newEntityKey.Type = _ENTITY_TYPE_TITLE_PLAYER_ACCOUNT;
                                            newPlayer._SetEntityKey(newEntityKey);
                                            if (_platformPolicyProvider != null)
                                            {
                                                _platformPolicyProvider.CreateOrUpdatePlatformUser(newPlayer, isLocal);
                                                _platformPolicyProvider.SendPlatformSpecificUserId(new List<PlayFabPlayer>() { newPlayer });
                                            }
                                            _remotePlayers.Add(newPlayer);
                                        }

                                        if (OnRemotePlayerJoined != null)
                                        {
                                            OnRemotePlayerJoined(this, newPlayer);
                                        }
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_ENDPOINT_DESTROYED:
                                {
                                    var stateChangeConverted = (PARTY_ENDPOINT_DESTROYED_STATE_CHANGE)stateChange;
                                    PartySucceeded(stateChangeConverted.errorDetail);

                                    PartySucceeded(SDK.PartyNetworkGetNetworkDescriptor(
                                        stateChangeConverted.network,
                                        out _networkDescriptor
                                        ));
                                    var remoteEndpoint = stateChangeConverted.endpoint;
                                    string oldPlayerEntityId = string.Empty;
                                    PartySucceeded(SDK.PartyEndpointGetEntityId(remoteEndpoint, out oldPlayerEntityId));

                                    bool isLocalPlayer = oldPlayerEntityId == _localPlayer.EntityKey.Id ? true : false;
                                    if (!isLocalPlayer)
                                    {
                                        PlayFabPlayer oldPlayer = GetPlayerByEntityId(oldPlayerEntityId) as PlayFabPlayer;
                                        if (oldPlayer != null)
                                        {
                                            _remotePlayers.Remove(oldPlayer);
                                        }

                                        if (OnRemotePlayerLeft != null)
                                        {
                                            OnRemotePlayerLeft(this, oldPlayer);
                                        }
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CHAT_CONTROL_CREATED:
                                {
                                    var stateChangeConverted = (PARTY_CHAT_CONTROL_CREATED_STATE_CHANGE)stateChange;
                                    PARTY_CHAT_CONTROL_HANDLE remoteChatControlHandle = stateChangeConverted.chatControl;
                                    string remoteChatControlEntityId = string.Empty;
                                    SDK.PartyChatControlGetEntityId(remoteChatControlHandle, out remoteChatControlEntityId);
                                    PlayFabPlayer player = GetPlayerByEntityId(remoteChatControlEntityId);
                                    if (player != null)
                                    {
                                        SetRemotePlayerChatControlHandle(player.EntityKey.Id, remoteChatControlHandle);
                                        UpdateCachedChatControlsList();
                                        if (!player.IsMuted &&
                                            !player._isLocal)
                                        {
                                            PARTY_CHAT_PERMISSION_OPTIONS chatPermissions = _CHAT_PERMISSIONS_ALL;
                                            if (_platformPolicyProvider != null)
                                            {
                                                chatPermissions = _platformPolicyProvider.GetChatPermissions(player);
                                            }
                                            PartySucceeded(SDK.PartyChatControlSetPermissions(
                                                    _localChatControlHandle,
                                                    remoteChatControlHandle,
                                                    chatPermissions
                                                ));
                                        }
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CHAT_CONTROL_DESTROYED:
                                {
                                    var stateChangeConverted = (PARTY_CHAT_CONTROL_DESTROYED_STATE_CHANGE)stateChange;
                                    PartySucceeded(stateChangeConverted.errorDetail);
                                    UpdateCachedChatControlsList();
                                }
                                break;
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CHAT_CONTROL_JOINED_NETWORK:
                                // No-op.
                                break;
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CHAT_CONTROL_LEFT_NETWORK:
                                {
                                    var stateChangeConverted = (PARTY_CHAT_CONTROL_LEFT_NETWORK_STATE_CHANGE)stateChange;
                                    PartySucceeded(stateChangeConverted.errorDetail);
                                }
                                break;
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CHAT_TEXT_RECEIVED:
                                {
                                    var stateChangeConverted = (PARTY_CHAT_TEXT_RECEIVED_STATE_CHANGE)stateChange;
                                    var remoteChatControl = stateChangeConverted.senderChatControl;
                                    string newPlayerEntityId = string.Empty;
                                    PartySucceeded(SDK.PartyChatControlGetEntityId(remoteChatControl, out newPlayerEntityId));
                                    PlayFabPlayer fromPlayer = GetPlayerByEntityId(newPlayerEntityId) as PlayFabPlayer;
                                    if (fromPlayer != null)
                                    {
                                        string chatText;
                                        if (stateChangeConverted.translations.Length > 0)
                                        {
                                            chatText = stateChangeConverted.translations[0].translation;
                                        }
                                        else
                                        {
                                            chatText = stateChangeConverted.chatText;
                                        }

                                        _RaiseChatMessageReceivedEvent(fromPlayer, chatText, ChatMessageType.Text);
                                    }
                                    else
                                    {
                                        _LogError(_ErrorMessagePlayerNotFound);
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CONFIGURE_AUDIO_MANIPULATION_CAPTURE_STREAM_COMPLETED:
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CONFIGURE_AUDIO_MANIPULATION_RENDER_STREAM_COMPLETED:
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CONFIGURE_AUDIO_MANIPULATION_VOICE_STREAM_COMPLETED:
                                {
                                    // No-op. The SDK does not currently support audio manipulation.
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CONNECT_CHAT_CONTROL_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_CONNECT_CHAT_CONTROL_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_CONNECT_TO_NETWORK_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_CONNECT_TO_NETWORK_COMPLETED_STATE_CHANGE)stateChange;
                                    if (InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail))
                                    {
                                        _networkDescriptor = stateChangeConverted.networkDescriptor;
                                        UpdateNetworkId(_generatedInvitationId, _networkDescriptor);
                                        AuthenticateLocalUserStart();
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_REMOTE_DEVICE_JOINED_NETWORK:
                                {
                                    // No-op. Players joining is covered when an endpoint joins.
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_REMOTE_DEVICE_LEFT_NETWORK:
                                {
                                    var stateChangeConverted = (PARTY_REMOTE_DEVICE_LEFT_NETWORK_STATE_CHANGE)stateChange;
                                    PartySucceeded(stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_NETWORK_DESCRIPTOR_CHANGED:
                                {
                                    var stateChangeConverted = (PARTY_NETWORK_DESCRIPTOR_CHANGED_STATE_CHANGE)stateChange;
                                    if (OnNetworkChanged != null)
                                    {
                                        PARTY_NETWORK_HANDLE newPartyNetworkHandle = stateChangeConverted.network;
                                        _networkHandle = newPartyNetworkHandle;
                                        PARTY_NETWORK_DESCRIPTOR newPartyNetworkDescriptor;
                                        SDK.PartyNetworkGetNetworkDescriptor(newPartyNetworkHandle, out newPartyNetworkDescriptor);
                                        PARTY_INVITATION_HANDLE[] newPartyInvitationHandles;
                                        SDK.PartyNetworkGetInvitations(newPartyNetworkHandle, out newPartyInvitationHandles);
                                        PARTY_INVITATION_CONFIGURATION newPartyInvitationConfiguration;
                                        string newPartyInvitationString = string.Empty;
                                        if (newPartyInvitationHandles.Length != 1)
                                        {
                                            if (PartySucceeded(SDK.PartyInvitationGetInvitationConfiguration(
                                                newPartyInvitationHandles[0],
                                                out newPartyInvitationConfiguration
                                                )))
                                            {
                                                newPartyInvitationString = newPartyInvitationConfiguration.Identifier;
                                            }
                                        }
                                        UpdateNetworkId(newPartyInvitationString, newPartyNetworkDescriptor);
                                        OnNetworkChanged(this, _networkId);
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_ENDPOINT_MESSAGE_RECEIVED:
                                {
                                    var stateChangeConverted = (PARTY_ENDPOINT_MESSAGE_RECEIVED_STATE_CHANGE)stateChange;
                                    var remoteEndpoint = stateChangeConverted.senderEndpoint;
                                    string newPlayerEntityId = string.Empty;
                                    PartySucceeded(SDK.PartyEndpointGetEntityId(remoteEndpoint, out newPlayerEntityId));
                                    PlayFabPlayer fromPlayer = GetPlayerByEntityId(newPlayerEntityId) as PlayFabPlayer;
                                    if (fromPlayer != null)
                                    {
                                        bool internalPlatformMessage = false;
                                        if (_platformPolicyProvider != null)
                                        {
                                            _platformPolicyProvider.ProcessEndpointMessage(fromPlayer, stateChangeConverted.messageBuffer, stateChangeConverted.messageSize, out internalPlatformMessage);
                                        }

                                        if (!internalPlatformMessage && !IsInternalMessage(stateChangeConverted.messageBuffer, stateChangeConverted.messageSize))
                                        {
                                            _RaiseDataMessageReceivedEvent(fromPlayer, stateChangeConverted.messageBuffer, stateChangeConverted.messageSize);
                                        }
                                    }
                                    else
                                    {
                                        _LogError(_ErrorMessagePlayerNotFound);
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_INVITATION_CREATED:
                                {
                                    // No-op. We don't expose an API to create invitations.
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_INVITATION_DESTROYED:
                                {
                                    var stateChangeConverted = (PARTY_INVITATION_DESTROYED_STATE_CHANGE)stateChange;
                                    PartySucceeded(stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_VOICE_CHAT_TRANSCRIPTION_RECEIVED:
                                {
                                    var stateChangeConverted = (PARTY_VOICE_CHAT_TRANSCRIPTION_RECEIVED_STATE_CHANGE)stateChange;
                                    if (InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail))
                                    {
                                        if (stateChangeConverted.type == PARTY_VOICE_CHAT_TRANSCRIPTION_PHRASE_TYPE.PARTY_VOICE_CHAT_TRANSCRIPTION_PHRASE_TYPE_FINAL)
                                        {
                                            var remoteChatControl = stateChangeConverted.senderChatControl;
                                            string newPlayerEntityId = string.Empty;
                                            PartySucceeded(SDK.PartyChatControlGetEntityId(remoteChatControl, out newPlayerEntityId));
                                            PlayFabPlayer fromPlayer;
                                            if (LocalPlayer.EntityKey.Id == newPlayerEntityId)
                                            {
                                                // receiving self-transcription
                                                fromPlayer = LocalPlayer;
                                            }
                                            else
                                            {
                                                fromPlayer = GetPlayerByEntityId(newPlayerEntityId) as PlayFabPlayer;
                                            }

                                            if (fromPlayer != null)
                                            {
                                                string chatText;
                                                if (stateChangeConverted.translations.Count > 0)
                                                {
                                                    chatText = stateChangeConverted.translations[0].translation;
                                                }
                                                else
                                                {
                                                    chatText = stateChangeConverted.transcription;
                                                }

                                                _RaiseChatMessageReceivedEvent(fromPlayer, chatText, ChatMessageType.SpeechToText);
                                            }
                                            else
                                            {
                                                _LogError(_ErrorMessagePlayerNotFound);
                                            }
                                        }
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_SET_LANGUAGE_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_SET_LANGUAGE_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_POPULATE_AVAILABLE_TEXT_TO_SPEECH_PROFILES_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_POPULATE_AVAILABLE_TEXT_TO_SPEECH_PROFILES_COMPLETED_STATE_CHANGE)stateChange;
                                    if (InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail))
                                    {
                                        PARTY_TEXT_TO_SPEECH_PROFILE_HANDLE[] profiles;
                                        PARTY_GENDER gender = PARTY_GENDER.PARTY_GENDER_NEUTRAL;
                                        string identifier = string.Empty;
                                        string languageCode = string.Empty;
                                        string name = string.Empty;
                                        if (PartySucceeded(SDK.PartyChatControlGetAvailableTextToSpeechProfiles(
                                                stateChangeConverted.localChatControl,
                                                out profiles
                                            )))
                                        {
                                            if (profiles.Length > 0)
                                            {
                                                PartySucceeded(SDK.PartyTextToSpeechProfileGetGender(profiles[0], out gender));
                                                PartySucceeded(SDK.PartyTextToSpeechProfileGetIdentifier(profiles[0], out identifier));
                                                PartySucceeded(SDK.PartyTextToSpeechProfileGetLanguageCode(profiles[0], out languageCode));
                                                PartySucceeded(SDK.PartyTextToSpeechProfileGetName(profiles[0], out name));
                                            }
                                        }
                                        PartySucceeded(SDK.PartyChatControlSetTextToSpeechProfile(
                                                stateChangeConverted.localChatControl,
                                                PARTY_SYNTHESIZE_TEXT_TO_SPEECH_TYPE.PARTY_SYNTHESIZE_TEXT_TO_SPEECH_TYPE_VOICE_CHAT,
                                                identifier,
                                                null
                                            ));
                                    }
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_SET_CHAT_AUDIO_INPUT_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_SET_CHAT_AUDIO_INPUT_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            case PARTY_STATE_CHANGE_TYPE.PARTY_STATE_CHANGE_TYPE_SET_CHAT_AUDIO_OUTPUT_COMPLETED:
                                {
                                    var stateChangeConverted = (PARTY_SET_CHAT_AUDIO_OUTPUT_COMPLETED_STATE_CHANGE)stateChange;
                                    InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                    break;
                                }
                            default:
                                // Throw a log info message about an unknown state.
                                break;
                        }
                    }
                    PartySucceeded(SDK.PartyFinishProcessingStateChanges(_partyHandle, _partyStateChanges));
                }
            }
        }

        public void ResetParty()
        {
            Debug.Log("ResetParty");
            _tasks.Clear();
            _runningTask = null;
            var mpManager = PlayFabMultiplayerManager.Get();
            if(mpManager.IsNotInitializedState() || mpManager.IsPendingInitializationState())
            {
                Debug.Log("No reinitialization required.");
                return ;
            }
            if(_networkId != null && mpManager.IsConnectedToNetworkState())
            {
                AddTask(new LeaveNetworkTask());
            }
            AddTask(new CleanPartyTask());
            AddTask(new InitPartyTask());
            if(_networkId != null && mpManager.IsConnectedToNetworkState())
            {
                AddTask(new JoinPartyTask(_networkId));
            }
        }

        private void AddTask(WorkTask task)
        {
            _tasks.Add(task);
        }

        private bool IsNotInitializedState()
        {
            return _playFabMultiplayerManagerState == _InternalPlayFabMultiplayerManagerState.NotInitialized;
        }

        private bool IsPendingInitializationState()
        {
            return _playFabMultiplayerManagerState == _InternalPlayFabMultiplayerManagerState.PendingInitialization;
        }

        private bool IsInitializedState()
        {
            return _playFabMultiplayerManagerState == _InternalPlayFabMultiplayerManagerState.Initialized;
        }

        private bool IsConnectedToNetworkState()
        {
            return _playFabMultiplayerManagerState == _InternalPlayFabMultiplayerManagerState.ConnectedToNetwork;
        }

        private abstract class WorkTask
        {
            // Used to determine whether task is executable.
            // If Begin() returns false, the task will not run.
            public abstract bool Begin();
            // Return true if the task is done. 
            public abstract bool Run();
            // Cleanup the task.
            public abstract void End();
        }

        private class LeaveNetworkTask : WorkTask
        {
            public override bool Begin()
            {
                Debug.Log("Task: LeaveNetworkTask");
                var mpManager = PlayFabMultiplayerManager.Get();
                if(mpManager.IsConnectedToNetworkState())
                {
                    mpManager.LeaveNetwork();
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public override bool Run()
            {
                var mpManager = PlayFabMultiplayerManager.Get();
                if(!mpManager.IsConnectedToNetworkState())
                {
                    return true;
                }
                return false;
            }

            public override void End()
            {
            }
        }

        private class CleanPartyTask : WorkTask
        {
            public override bool Begin()
            {
                Debug.Log("Task: CleanPartyTask");
                var mpManager = PlayFabMultiplayerManager.Get();
                if(!mpManager.IsNotInitializedState())
                {
                    mpManager._CleanUp();
                }
                return true;
            }

            public override bool Run()
            {
                var mpManager = PlayFabMultiplayerManager.Get();
                if(mpManager.IsNotInitializedState())
                {
                    return true;
                }
                return false;
            }

            public override void End()
            {
            }
        }

        private class InitPartyTask : WorkTask
        {
            public override bool Begin()
            {
                Debug.Log("Task: InitPartyTask()");
                var mpManager = PlayFabMultiplayerManager.Get();
                if(!mpManager.IsInitializedState())
                {
                    mpManager._Initialize();
                    return true;
                }
                return false;
            }

            public override bool Run()
            {
                var mpManager = PlayFabMultiplayerManager.Get();
                if(mpManager.IsInitializedState())
                {
                    return true;
                }
                return false;
            }

            public override void End()
            {
            }
        }

        private class JoinPartyTask : WorkTask
        {
            private string _networkId;

            public JoinPartyTask(string networkId)
            {
                _networkId = networkId;
            }

            public override bool Begin()
            {
                Debug.Log("Task: JoinPartyTask");
                var mpManager = PlayFabMultiplayerManager.Get();
                if(!mpManager.IsConnectedToNetworkState())
                {
                    mpManager.JoinNetwork(_networkId);
                    return true;
                }
                return false;
            }

            public override bool Run()
            {
                var mpManager = PlayFabMultiplayerManager.Get();
                if(mpManager.IsConnectedToNetworkState())
                {
                    return true;
                }
                return false;
            }

            public override void End()
            {
            }
        }

        private void ProcessTask()
        {
            if(_runningTask == null)
            {
                while(_tasks.Count > 0)
                {
                    _runningTask = _tasks[0];
                    _tasks.RemoveAt(0);
                    if(_runningTask.Begin())
                    {
                        break;
                    }
                }
            }
            else
            {
                if(_runningTask.Run())
                {
                    _runningTask.End();
                    _runningTask = null;
                }
            }
        }

        private bool HasTasks()
        {
            if(_runningTask != null)
            {
                return true;
            }
            return _tasks.Count > 0;
        }

        internal enum _InternalPlayFabMultiplayerManagerState
        {
            NotInitialized,
            PendingInitialization,
            Initialized,
            LoginRequestIssued,
            LocalUserCreated,
            LocalUserAuthenticated,
            ConnectedToNetwork
        }

        private struct QueuedStartCreateAndJoinNetworkOp
        {
            public bool queued;
            public PlayFabNetworkConfiguration networkConfiguration;
        }

        private struct QueuedCreateAndJoinAfterLeaveNetworkOp
        {
            public bool queued;
            public PlayFabNetworkConfiguration networkConfiguration;
        }

        private struct QueuedJoinNetworkOp
        {
            public bool queued;
            public string networkId;
        }

        private struct QueuedCompleteJoinAfterLeaveNetworkOp
        {
            public bool queued;
            public string networkId;
        }

        /// <summary>
        /// The amount of logging that is enabled.
        /// </summary>
        public enum LogLevelType
        {
            None,
            Minimal,
            Verbose
        }

        private enum PlayFabMultiplayerManagerMessageType : sbyte
        {
            Unset = 0,
            Game = 1,
            PolicyManager = 2
        }
    }
}
