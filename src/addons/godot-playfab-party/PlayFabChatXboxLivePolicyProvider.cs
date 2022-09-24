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
using System.Runtime.InteropServices;
using System.Text;
using PartyCSharpSDK;
using PartyXBLCSharpSDK;
using PlayFab.ClientModels;

#if UNITY_GAMECORE
using UnityEngine.GameCore;
using Unity.GameCore;
using XGR = Unity.GameCore;
#endif
#if MICROSOFT_GAME_CORE
using XGamingRuntime;
using XGR = XGamingRuntime;
#endif

#if BUILD_XBL_PLUGIN
namespace PlayFab.Party._Internal
{
    /// <summary>
    /// The Party voice and text messages policy provider to be used when using Xbox Live player authentication.
    /// Using this policy provider helps game comply with Microsoft Game Core XR-015 and XR-045 requirements.
    /// </summary>
    internal class PlayFabChatXboxLivePolicyProvider : IPlayFabChatPlatformPolicyProvider
    {
        private PARTY_XBL_HANDLE _xblPartyHandle;
        private PlayFabMultiplayerManager _multiplayerManager;
        private PARTY_XBL_CHAT_USER_HANDLE _xblLocalChatUserHandle;

        private static PlayFabChatXboxLivePolicyProvider _xblPolicyProvider;
        private QueuedUpdateChatPermissionsOp _queuedUpdateChatPermissionsOp;
        private XUserHandle _xblLocalUserHandle;
        private Dictionary<PlayFabPlayer, PARTY_XBL_CHAT_PERMISSION_INFO> _playerChatPermissions;
        private List<QueuedCreateRemoteXboxLiveChatUserOp> _queuedCreateRemoteXboxLiveChatUserOps;
        private List<PARTY_XBL_STATE_CHANGE> _xblStateChanges;
        private byte[] _internalXuidExchangeMessageBuffer;

        private byte[] _XUID_EXCHANGE_REQUEST_AS_BYTES;
        private byte[] _XUID_EXCHANGE_RESPONSE_AS_BYTES;

        private const PARTY_CHAT_PERMISSION_OPTIONS _CHAT_PERMISSIONS_ALL =
            PARTY_CHAT_PERMISSION_OPTIONS.PARTY_CHAT_PERMISSION_OPTIONS_SEND_AUDIO |
            PARTY_CHAT_PERMISSION_OPTIONS.PARTY_CHAT_PERMISSION_OPTIONS_RECEIVE_AUDIO |
            PARTY_CHAT_PERMISSION_OPTIONS.PARTY_CHAT_PERMISSION_OPTIONS_RECEIVE_TEXT;
        private const uint _INTERNAL_XUID_EXCHANGE_MESSAGE_BUFFER_SIZE = 128;
        private const string _XUID_EXCHANGE_REQUEST_MESSAGE_PREFIX = "PFP-XBL-XUID-REQUEST";
        private const string _XUID_EXCHANGE_RESPONSE_MESSAGE_PREFIX = "PFP-XBL-XUID-RESPONSE";
        private const int _E_GAMEUSER_RESOLVE_USER_ISSUE_REQUIRED = -1994108670;

        private const string _ErrorMessageGamingRuntimeNotInitialized = "Gaming Runtime not initialized. You need to call SDK.XGameRuntimeInitialize()";
        private const string _ErrorMessageCouldNotGetXuid = "Could not get a XUID.";
        private const string _ErrorMessageCouldNotGetXboxLiveToken = "Could not get an Xbox Live token.";
        private const string _ErrorMessageXboxLiveSignInFailed = "Xbox Live sign in failed.";

        public static PlayFabChatXboxLivePolicyProvider Get()
        {
            if (_xblPolicyProvider == null)
            {
                _xblPolicyProvider = new PlayFabChatXboxLivePolicyProvider();
            }
            return _xblPolicyProvider;
        }

        public PlayFabChatXboxLivePolicyProvider()
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:PlayFabChatXboxLivePolicyProvider()");

            _multiplayerManager = PlayFabMultiplayerManager.Get();
            string titleId = PlayFabSettings.staticSettings.TitleId;

            _playerChatPermissions = new Dictionary<PlayFabPlayer, PARTY_XBL_CHAT_PERMISSION_INFO>(new PlayerComparator());
            _queuedCreateRemoteXboxLiveChatUserOps = new List<QueuedCreateRemoteXboxLiveChatUserOp>();
            _xblStateChanges = new List<PARTY_XBL_STATE_CHANGE>();
            _internalXuidExchangeMessageBuffer = new byte[_INTERNAL_XUID_EXCHANGE_MESSAGE_BUFFER_SIZE];

            Succeeded(XBLSDK.PartyXblInitialize(titleId, out _xblPartyHandle));

            _XUID_EXCHANGE_REQUEST_AS_BYTES = Encoding.ASCII.GetBytes(_XUID_EXCHANGE_REQUEST_MESSAGE_PREFIX);
            _XUID_EXCHANGE_RESPONSE_AS_BYTES = Encoding.ASCII.GetBytes(_XUID_EXCHANGE_RESPONSE_MESSAGE_PREFIX);
        }

        public bool CleanUp()
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:CleanUp()");

            bool succeeded = true;
            succeeded = Succeeded(XBLSDK.PartyXblCleanup(_xblPartyHandle));

            _xblPolicyProvider = null;
            _playerChatPermissions = null;
            _xblStateChanges = null;
            _queuedCreateRemoteXboxLiveChatUserOps = null;
            _internalXuidExchangeMessageBuffer = null;
            _XUID_EXCHANGE_REQUEST_AS_BYTES = null;
            _XUID_EXCHANGE_RESPONSE_AS_BYTES = null;
            _xblLocalChatUserHandle = null;
            _xblPartyHandle = null;
            return succeeded;
        }

        public void SignIn()
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:SignIn()");

            try
            {
                XGR.SDK.XUserAddAsync(XUserAddOptions.AddDefaultUserSilently, SignInSilentlyComplete);
            }
            catch (NullReferenceException)
            {
                PlayFabMultiplayerManager._LogError(_ErrorMessageGamingRuntimeNotInitialized);
            }
            catch (Exception ex)
            {
                PlayFabMultiplayerManager._LogError(ex.Message);
            }
        }

        public void CreateOrUpdatePlatformUser(PlayFabPlayer player, bool isLocal)
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:CreateOrUpdatePlatformUser()");

            if (isLocal)
            {
                ulong xuid;
#if (MICROSOFT_GAME_CORE || UNITY_GAMECORE)
                if (HrSucceeded(XGR.SDK.XUserGetId(_xblLocalUserHandle, out xuid)))
#endif
                {
                    player._platformSpecificUserId = xuid.ToString();
                }
            }
            else
            {
                TryCreateRemoteXboxLiveChatUser(player);
            }
        }

        public PARTY_CHAT_PERMISSION_OPTIONS GetChatPermissions(PlayFabPlayer targetPlayer)
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:GetChatPermissions()");

            PARTY_CHAT_PERMISSION_OPTIONS chatPermissions = _CHAT_PERMISSIONS_ALL;
            if (_playerChatPermissions.ContainsKey(targetPlayer))
            {
                chatPermissions = _playerChatPermissions[targetPlayer].ChatPermissionMask;
            }
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:get chat permissions for EntityId: " + targetPlayer.EntityKey.Id + ", chat permissions: " + chatPermissions);

            return chatPermissions;
        }

        public void ProcessEndpointMessage(PlayFabPlayer fromPlayer, IntPtr messageBuffer, uint messageSize, out bool isInternalMessage)
        {
            isInternalMessage = false;
            // Another client asks for our client's XUID
            if (messageSize > 0 &&
                messageSize < _INTERNAL_XUID_EXCHANGE_MESSAGE_BUFFER_SIZE)
            {
                Marshal.Copy(messageBuffer, _internalXuidExchangeMessageBuffer, 0, (int)messageSize);
                if (_multiplayerManager._StartsWithSequence(_internalXuidExchangeMessageBuffer, _XUID_EXCHANGE_REQUEST_AS_BYTES))
                {
                    isInternalMessage = true;
                    PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider: received remote XUID.");

                    // Find the other client and set their XIUD

                    // The buffer can contain trailing bytes if the application has received any message
                    // that passed the first if check in this function. (messageSize > 0 && messageSize < _INTERNAL_XUID_EXCHANGE_MESSAGE_BUFFER_SIZE)
                    // Alternative to this would be to clear _internalXuidExchangeMessageBuffer before copying the messageBuffer into it.
                    uint remoteXuidAsBytesLength = messageSize - (uint)_XUID_EXCHANGE_REQUEST_AS_BYTES.Length - 1;
                    if (remoteXuidAsBytesLength >= 0)
                    {
                        byte[] remoteXuidAsBytes = new byte[remoteXuidAsBytesLength];
                        Array.Copy(_internalXuidExchangeMessageBuffer, _XUID_EXCHANGE_REQUEST_AS_BYTES.Length + 1, remoteXuidAsBytes, 0, remoteXuidAsBytes.Length);
                        fromPlayer._platformSpecificUserId = Encoding.ASCII.GetString(remoteXuidAsBytes);

                        PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider: sent XUID exchange response message.");

                        // Update queued operations
                        for (int i = 0; i < _queuedCreateRemoteXboxLiveChatUserOps.Count; i++)
                        {
                            if (_queuedCreateRemoteXboxLiveChatUserOps[i].otherPlayer.EntityKey.Id == fromPlayer.EntityKey.Id)
                            {
                                ulong remoteXuid = Convert.ToUInt64(fromPlayer._platformSpecificUserId);
                                _queuedCreateRemoteXboxLiveChatUserOps[i].xuid = remoteXuid;
                                break;
                            }
                        }
                    }
                }
            }
        }

        public PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS GetPlatformUserChatTranscriptionPreferences()
        {
            PARTY_XBL_ACCESSIBILITY_SETTINGS accessibilitySettings;
            PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS partyChatTranscriptionOptions = PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS.PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS_NONE;
            Succeeded(XBLSDK.PartyXblLocalChatUserGetAccessibilitySettings(
                    _xblLocalChatUserHandle,
                    out accessibilitySettings
                ));
            if (accessibilitySettings != null &&
                accessibilitySettings.SpeechToTextEnabled != 0)
            {
                partyChatTranscriptionOptions = PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS.PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS_TRANSCRIBE_OTHER_CHAT_CONTROLS_WITH_MATCHING_LANGUAGES |
                    PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS.PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS_TRANSCRIBE_SELF;
            }
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:GetPlatformUserChatTranscriptionPreferences(), transcription options: " + partyChatTranscriptionOptions);

            return partyChatTranscriptionOptions;
        }

        public bool IsTextToSpeechEnabled()
        {
            bool isTextToSpeechEnabled = false;
            PARTY_XBL_ACCESSIBILITY_SETTINGS accessibilitySettings;
            Succeeded(XBLSDK.PartyXblLocalChatUserGetAccessibilitySettings(
            _xblLocalChatUserHandle,
            out accessibilitySettings
        ));
            if (accessibilitySettings != null &&
                accessibilitySettings.TextToSpeechEnabled != 0)
            {
                isTextToSpeechEnabled = true;
            }
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:IsTextToSpeechEnabled(), value: " + isTextToSpeechEnabled);

            return isTextToSpeechEnabled;
        }

        public void SendPlatformSpecificUserId(List<PlayFabPlayer> targetPlayers)
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:SendPlatformSpecificUserId()");

            // Broadcast XUID to other endpoints in the network.
            ulong xuid;
#if (MICROSOFT_GAME_CORE || UNITY_GAMECORE)
            if (HrSucceeded(XGR.SDK.XUserGetId(_xblLocalUserHandle, out xuid)))
#endif
            {
                string xuidMessageString = _XUID_EXCHANGE_REQUEST_MESSAGE_PREFIX + ":" + xuid;
                byte[] xuidMessageBytes = Encoding.ASCII.GetBytes(xuidMessageString);
                _multiplayerManager._SendDataMessage(xuidMessageBytes, targetPlayers, DeliveryOption.Guaranteed);
            }
            else
            {
                PlayFabMultiplayerManager._LogError(_ErrorMessageCouldNotGetXuid);
            }
        }

        public void ProcessQueuedOperations()
        {
            if (_queuedUpdateChatPermissionsOp.queued &&
                IsReadyToSetChatPermissions(_queuedUpdateChatPermissionsOp.localXblChatUser, _queuedUpdateChatPermissionsOp.targetXblChatUser))
            {
                UpdateChatPermissionInfoComplete(
                    _queuedUpdateChatPermissionsOp.localXblChatUser,
                    _queuedUpdateChatPermissionsOp.targetXblChatUser
                    );
            }
            for (int i = _queuedCreateRemoteXboxLiveChatUserOps.Count - 1; i >= 0; i--)
            {
                if (_queuedCreateRemoteXboxLiveChatUserOps[i].xuid != 0)
                {
                    TryCreateRemoteXboxLiveChatUser(_queuedCreateRemoteXboxLiveChatUserOps[i].otherPlayer);
                }
            }
        }

        public void ProcessStateChanges()
        {
            if (Succeeded(XBLSDK.PartyXblStartProcessingStateChanges(_xblPartyHandle, out _xblStateChanges)))
            {
                foreach (PARTY_XBL_STATE_CHANGE stateChange in _xblStateChanges)
                {
                    PlayFabMultiplayerManager._LogInfo("XBL State change: " + stateChange.StateChangeType.ToString());
                    switch (stateChange.StateChangeType)
                    {
                        case PARTY_XBL_STATE_CHANGE_TYPE.PARTY_XBL_STATE_CHANGE_TYPE_TOKEN_AND_SIGNATURE_REQUESTED:
                            {
                                var stateChangeConverted = (PARTY_XBL_TOKEN_AND_SIGNATURE_REQUESTED_STATE_CHANGE)stateChange;
                                TrackableGetXTokenCompletedWrapper trackableGetXTokenCompletedWrapper = new TrackableGetXTokenCompletedWrapper();
                                trackableGetXTokenCompletedWrapper.correlationId = stateChangeConverted.correlationId;
                                trackableGetXTokenCompletedWrapper.url = stateChangeConverted.url;
                                trackableGetXTokenCompletedWrapper.method = stateChangeConverted.method;
                                trackableGetXTokenCompletedWrapper.headers = stateChangeConverted.headers;
                                trackableGetXTokenCompletedWrapper.body = stateChangeConverted.body;
                                XUserGetTokenAndSignatureOptions options = XUserGetTokenAndSignatureOptions.None;
                                if (stateChangeConverted.allUsers)
                                {
                                    options |= XUserGetTokenAndSignatureOptions.AllUsers;
                                }
                                if (stateChangeConverted.forceRefresh)
                                {
                                    options |= XUserGetTokenAndSignatureOptions.ForceRefresh;
                                }
                                XUserGetTokenAndSignatureUtf16HttpHeader[] tokenRequestHeaders = new XUserGetTokenAndSignatureUtf16HttpHeader[stateChangeConverted.headers.Length];
                                for (uint i = 0; i < stateChangeConverted.headers.Length; i++)
                                {
                                    string tokenRequestHeaderName = stateChangeConverted.headers[i].name;
                                    string tokenRequestHeaderValue = stateChangeConverted.headers[i].value;
                                    tokenRequestHeaders[i] = new XUserGetTokenAndSignatureUtf16HttpHeader(tokenRequestHeaderName, tokenRequestHeaderValue);
                                }

                                var headers = tokenRequestHeaders;
                                if (stateChangeConverted.headers.Length == 0)
                                {
                                    headers = null;
                                }
                                var body = stateChangeConverted.body;
                                if (stateChangeConverted.body.Length == 0)
                                {
                                    body = null;
                                }
#if (MICROSOFT_GAME_CORE || UNITY_GAMECORE)
                                XGR.SDK.XUserGetTokenAndSignatureUtf16Async(
                                        _xblLocalUserHandle,
                                        options,
                                        stateChangeConverted.method,
                                        stateChangeConverted.url,
                                        headers,
                                        body,
                                        trackableGetXTokenCompletedWrapper.CompleteGetXToken
                                        );
#endif
                                break;
                            }
                        case PARTY_XBL_STATE_CHANGE_TYPE.PARTY_XBL_STATE_CHANGE_TYPE_CREATE_LOCAL_CHAT_USER_COMPLETED:
                            {
                                var stateChangeConverted = (PARTY_XBL_CREATE_LOCAL_CHAT_USER_COMPLETED_STATE_CHANGE)stateChange;
                                _multiplayerManager.InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail);
                                break;
                            }
                        case PARTY_XBL_STATE_CHANGE_TYPE.PARTY_XBL_STATE_CHANGE_TYPE_LOGIN_TO_PLAYFAB_COMPLETED:
                            {
                                var stateChangeConverted = (PARTY_XBL_LOGIN_TO_PLAYFAB_COMPLETED_STATE_CHANGE)stateChange;
                                if (_multiplayerManager.InternalCheckStateChangeSucceededOrLogErrorIfFailed(stateChangeConverted.result, stateChangeConverted.errorDetail))
                                {
                                    OnPlayFabLoginSuccess(stateChangeConverted);
                                }
                                break;
                            }
                        case PARTY_XBL_STATE_CHANGE_TYPE.PARTY_XBL_STATE_CHANGE_TYPE_LOCAL_CHAT_USER_DESTROYED:
                            break;
                        case PARTY_XBL_STATE_CHANGE_TYPE.PARTY_XBL_STATE_CHANGE_TYPE_REQUIRED_CHAT_PERMISSION_INFO_CHANGED:
                            {
                                var stateChangeConverted = (PARTY_XBL_REQUIRED_CHAT_PERMISSION_INFO_CHANGED_STATE_CHANGE)stateChange;
                                PARTY_XBL_CHAT_USER_HANDLE localXblChatUser = stateChangeConverted.localChatUser;
                                PARTY_XBL_CHAT_USER_HANDLE targetXblChatUser = stateChangeConverted.targetChatUser;
                                UpdateChatPermissionInfoStart(localXblChatUser, targetXblChatUser);
                            }
                            break;
                        default:
                            break;
                    }
                }
                Succeeded(XBLSDK.PartyXblFinishProcessingStateChanges(_xblPartyHandle, _xblStateChanges));
            }
        }

        private void UpdateChatPermissionInfoStart(PARTY_XBL_CHAT_USER_HANDLE localXblChatUser, PARTY_XBL_CHAT_USER_HANDLE targetXblChatUser)
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:UpdateChatPermissionInfoStart()");

            if (IsReadyToSetChatPermissions(localXblChatUser, targetXblChatUser))
            {
                UpdateChatPermissionInfoComplete(localXblChatUser, targetXblChatUser);
            }
            else
            {
                _queuedUpdateChatPermissionsOp = new QueuedUpdateChatPermissionsOp()
                {
                    queued = true,
                    localXblChatUser = localXblChatUser,
                    targetXblChatUser = targetXblChatUser
                };
            }
        }

        private bool IsReadyToSetChatPermissions(PARTY_XBL_CHAT_USER_HANDLE localXblChatUser, PARTY_XBL_CHAT_USER_HANDLE targetXblChatUser)
        {
            bool isReadyToSetChatPermissions = false;
            ulong localXuid;
            ulong targetXuid;
            if (!Succeeded(XBLSDK.PartyXblChatUserGetXboxUserId(localXblChatUser, out localXuid)))
            {
                return false;
            }
            if (!Succeeded(XBLSDK.PartyXblChatUserGetXboxUserId(targetXblChatUser, out targetXuid)))
            {
                return false;
            }

            isReadyToSetChatPermissions = GetPlayerByXuid(localXuid) != null &&
            GetPlayerByXuid(targetXuid) != null;

            return isReadyToSetChatPermissions;
        }

        private void UpdateChatPermissionInfoComplete(PARTY_XBL_CHAT_USER_HANDLE localXblChatUser, PARTY_XBL_CHAT_USER_HANDLE targetXblChatUser)
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:UpdateChatPermissionInfoComplete()");

            // UpdateChatPermissionInfoComplete() is called when we are finally ready to set permissions, 
            // and thus the cached operation in _queuedUpdateChatPermissionsOp is no longer needed
            // (if it was created then localXblChatUser and targetXblChatUser from it will be used in this method
            // to set permissions). It needs to be reset to prevent unnecessary continuous re-entrance to this method.
            if (_queuedUpdateChatPermissionsOp.queued)
            {
                _queuedUpdateChatPermissionsOp = new QueuedUpdateChatPermissionsOp(); // reset _queuedUpdateChatPermissionsOp to its initial (default) state
            }

            ulong localXuid;
            ulong targetXuid;
            if (!Succeeded(XBLSDK.PartyXblChatUserGetXboxUserId(localXblChatUser, out localXuid)))
            {
                return;
            }
            if (!Succeeded(XBLSDK.PartyXblChatUserGetXboxUserId(targetXblChatUser, out targetXuid)))
            {
                return;
            }
            PlayFabPlayer localPlayer = GetPlayerByXuid(localXuid);
            PlayFabPlayer targetPlayer = GetPlayerByXuid(targetXuid);
            if (localPlayer == null || targetPlayer == null)
            {
                return;
            }

            PARTY_XBL_CHAT_PERMISSION_INFO chatPermissionInfo;
            Succeeded(XBLSDK.PartyXblLocalChatUserGetRequiredChatPermissionInfo(
                    localXblChatUser,
                    targetXblChatUser,
                    out chatPermissionInfo
                ));

            Succeeded(PartyCSharpSDK.SDK.PartyChatControlSetPermissions(
                    localPlayer._chatControlHandle,
                    targetPlayer._chatControlHandle,
                    chatPermissionInfo.ChatPermissionMask
                ));

            var permissionsQuery = chatPermissionInfo.ChatPermissionMask;
            bool mute = permissionsQuery != _CHAT_PERMISSIONS_ALL;
            foreach (var playerKeyPair in _multiplayerManager.RemotePlayers)
            {
                if (playerKeyPair.EntityKey.Id == targetPlayer.EntityKey.Id)
                {
                    playerKeyPair._mutedByPlatform = mute;
                    playerKeyPair.IsMuted = mute;
                    break;
                }
            }
            if (!_playerChatPermissions.ContainsKey(targetPlayer))
            {
                _playerChatPermissions.Add(targetPlayer, chatPermissionInfo);
            }
        }

        private void OnPlayFabLoginSuccess(PARTY_XBL_LOGIN_TO_PLAYFAB_COMPLETED_STATE_CHANGE loginResult)
        {
            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:OnPlayFabLoginSuccess(), EntityId: " + loginResult.entityId);

            EntityKey entityKey = new EntityKey()
            {
                Id = loginResult.entityId,
                Type = "title_player_account"
            };
            _multiplayerManager._CreateLocalUser(entityKey, loginResult.titlePlayerEntityToken);
        }

        private void SignInSilentlyComplete(int hresult, XUserHandle userHandle)
        {
            if (HrSucceeded(hresult))
            {
                _xblLocalUserHandle = userHandle;
            }
            else
            {
                PlayFabMultiplayerManager._LogError(_ErrorMessageXboxLiveSignInFailed);
            }

            ulong xuid;
            int hr = 0;
#if (MICROSOFT_GAME_CORE || UNITY_GAMECORE)
            hr = XGR.SDK.XUserGetId(_xblLocalUserHandle, out xuid);
#endif

            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:SignInSilentlyComplete(), XUID: " + xuid);

            if (HrSucceeded(hresult))
            {
                if (_xblLocalChatUserHandle == null)
                {
                    Succeeded(XBLSDK.PartyXblCreateLocalChatUser(
                       _xblPartyHandle,
                       xuid,
                       null,
                       out _xblLocalChatUserHandle
                   ));
                }

                Succeeded(XBLSDK.PartyXblLoginToPlayFab(
                    _xblLocalChatUserHandle,
                    null
                ));
            }
            else
            {
                PlayFabMultiplayerManager._LogError(_ErrorMessageCouldNotGetXuid);
            }
        }

        private void TryCreateRemoteXboxLiveChatUser(PlayFabPlayer otherPlayer)
        {
            if (string.IsNullOrEmpty(otherPlayer._platformSpecificUserId))
            {
                _queuedCreateRemoteXboxLiveChatUserOps.Add(new QueuedCreateRemoteXboxLiveChatUserOp()
                {
                    otherPlayer = otherPlayer
                });
                return;
            }
            ulong xuid = Convert.ToUInt64(otherPlayer._platformSpecificUserId);

            PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:TryCreateRemoteXboxLiveChatUser(), XUID: " + xuid);

            if (Succeeded(XBLSDK.PartyXblCreateRemoteChatUser(
                    _xblPartyHandle,
                    xuid,
                    out otherPlayer._xblChatUserHandle
                )))
            {
                for (int i = 0; i < _queuedCreateRemoteXboxLiveChatUserOps.Count; i++)
                {
                    if (xuid == _queuedCreateRemoteXboxLiveChatUserOps[i].xuid)
                    {
                        _queuedCreateRemoteXboxLiveChatUserOps.RemoveAt(i);
                        break;
                    }
                }


            }
        }

        private PlayFabPlayer GetPlayerByXuid(ulong xuid)
        {
            if (xuid == 0)
            {
                return null;
            }

            // check local player first
            if (_multiplayerManager.LocalPlayer != null && !string.IsNullOrEmpty(_multiplayerManager.LocalPlayer._platformSpecificUserId))
            {
                ulong localPlayerXuid = Convert.ToUInt64(_multiplayerManager.LocalPlayer._platformSpecificUserId);
                if (localPlayerXuid == xuid)
                {
                    return _multiplayerManager.LocalPlayer;
                }
            }

            // then check all remote players
            PlayFabPlayer player = null;
            foreach (PlayFabPlayer currentPlayer in _multiplayerManager.RemotePlayers)
            {
                if (string.IsNullOrEmpty(currentPlayer._platformSpecificUserId))
                {
                    continue;
                }

                ulong currentPlayerXuid = Convert.ToUInt64(currentPlayer._platformSpecificUserId);
                if (currentPlayerXuid == xuid)
                {
                    player = currentPlayer;
                    break;
                }
            }
            return player;
        }

        // Helper methods
        private bool Succeeded(uint errorCode)
        {
            return _multiplayerManager.PartySucceeded(errorCode);
        }

        private bool HrSucceeded(int hresult)
        {
            return hresult >= 0;
        }

        private class PlayerComparator : IEqualityComparer<PlayFabPlayer>
        {
            public bool Equals(PlayFabPlayer a, PlayFabPlayer b)
            {
                return a.EntityKey.Id == b.EntityKey.Id ? true : false;
            }
            public int GetHashCode(PlayFabPlayer player)
            {
                return player.GetHashCode();
            }
        }

        private class TrackableGetXTokenCompletedWrapper
        {
            public uint correlationId;
            public string method;
            public string url;
            public byte[] body;
            public PARTY_XBL_HTTP_HEADER[] headers;

            private static bool _pendingResolveIssueWithUICallback;

            public void CompleteGetXToken(int hresult, XUserGetTokenAndSignatureUtf16Data tokenData)
            {
                PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:CompleteGetXToken(), hr: " + hresult);

                if (hresult >= 0)
                {
                    Get().Succeeded(XBLSDK.PartyXblCompleteGetTokenAndSignatureRequest(
                        Get()._xblPartyHandle,
                        correlationId,
                        true,
                        tokenData.Token,
                        tokenData.Signature
                        ));
                }
                else if (hresult == _E_GAMEUSER_RESOLVE_USER_ISSUE_REQUIRED)
                {
                    // We need to resolve
#if (MICROSOFT_GAME_CORE || UNITY_GAMECORE)
                    XGR.SDK.XUserResolveIssueWithUiUtf16Async(
                            Get()._xblLocalUserHandle,
                            url,
                            _ResolveUserIssueWithUICompleted
                        );
#endif
                }
                else
                {
                    PlayFabMultiplayerManager._LogError(_ErrorMessageCouldNotGetXboxLiveToken);
                }
            }

            private void _ResolveUserIssueWithUICompleted(int hresult)
            {
                PlayFabMultiplayerManager._LogInfo("PlayFabChatXboxLivePolicyProvider:_ResolveUserIssueWithUICompleted(), hr: " + hresult);

                if (_pendingResolveIssueWithUICallback)
                {
                    return;
                }
                _pendingResolveIssueWithUICallback = true;

                if (Get().HrSucceeded(hresult))
                {
                    Get().SignIn();
                }
            }
        }

        private class QueuedCreateRemoteXboxLiveChatUserOp
        {
            public PlayFabPlayer otherPlayer;
            public ulong xuid;
        }

        private struct QueuedUpdateChatPermissionsOp
        {
            public bool queued;
            public PARTY_XBL_CHAT_USER_HANDLE localXblChatUser;
            public PARTY_XBL_CHAT_USER_HANDLE targetXblChatUser;
        }

        private enum XboxPolicyMessageType : sbyte
        {
            Unset = 0,
            XuidExchangeRequest = 1,
            XuidExchangeResponse = 2
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct XboxPolicyXuidExchangeResponseMessage
        {
            XboxPolicyMessageType type;
            UInt16 xuid;
        }
    }
}
#endif