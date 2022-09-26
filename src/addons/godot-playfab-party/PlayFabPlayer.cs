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

using PartyCSharpSDK;
using PartyXBLCSharpSDK;
using PlayFab.ClientModels;

namespace PlayFab.Party
{
    /// <summary>
    /// A class representing a player in the network.
    /// </summary>
    public class PlayFabPlayer
    {
        private ChatState _chatState;
        private float _voiceLevel;
        private bool _isMuted;

        internal string _entityToken;
        internal bool _isLocal;
        internal string _platformSpecificUserId;
        internal PARTY_ENDPOINT_HANDLE _endPointHandle;
        internal PARTY_CHAT_CONTROL_HANDLE _chatControlHandle;
        internal bool _mutedByPlatform;
#if (MICROSOFT_GAME_CORE || UNITY_GAMECORE) && !UNITY_EDITOR
        internal PARTY_XBL_CHAT_USER_HANDLE _xblChatUserHandle;
#endif

        private const string _ErrorMessageVoiceLevelOutOfRange = "Value {0} is out of range. Value must be between 0 and 1.";

        /// <summary>
        /// Ctor
        /// </summary>
        public PlayFabPlayer()
        {
            _platformSpecificUserId = string.Empty;
            _mutedByPlatform = false;
        }

        internal void _SetEntityKey(EntityKey entityKey)
        {
            EntityKey = entityKey;
        }
             
        /// <summary>
        /// Gets the visual chat state of this player for display in your game UI.
        /// </summary>
        public ChatState ChatState
        {
            get
            {
                PlayFabMultiplayerManager playFabMultiplayerManager = PlayFabMultiplayerManager.Get();
                _chatState = playFabMultiplayerManager._GetChatState(EntityKey, _isLocal);
                return _chatState;
            }
        }

        /// <summary>
        /// Gets the Entity Key of the player.
        /// </summary>
        public EntityKey EntityKey
        {
            get;
            private set;
        }

        /// <summary>
        /// Returns true if this player is the local player and false if this player is a remote player.
        /// </summary>
        public bool IsLocal
        {
            get
            {
                return _isLocal;
            }
        }

        /// <summary>
        /// Gets or sets whether the player is muted.
        /// </summary>
        public bool IsMuted
        {
            get
            {
                return _isMuted;
            }
            set
            {
                PlayFabMultiplayerManager playFabMultiplayerManager = PlayFabMultiplayerManager.Get();
                playFabMultiplayerManager._SetMuted(EntityKey, value, _isLocal);
                _isMuted = value;
            }
        }

        /// <summary>
        /// Gets or sets the volume of the player. The value is a float between 0 and 1.
        /// </summary>
        public float VoiceLevel
        {
            get
            {
                PlayFabMultiplayerManager playFabMultiplayerManager = PlayFabMultiplayerManager.Get();
                if (_isLocal)
                {
                    return _voiceLevel;
                }
                else
                {
                    _voiceLevel = playFabMultiplayerManager._GetVoiceLevel(EntityKey);
                }
                return _voiceLevel;
            }
            set
            {
                if (value >= 0 &&
                    value <= 1)
                {
                    PlayFabMultiplayerManager playFabMultiplayerManager = PlayFabMultiplayerManager.Get();
                    playFabMultiplayerManager._SetVoiceLevel(EntityKey, value, _isLocal);
                    _voiceLevel = value;
                }
                else
                {
                    PlayFabMultiplayerManager._LogError(_ErrorMessageVoiceLevelOutOfRange.Replace("{0}", value.ToString()));
                }
            }
        }
    }
}
