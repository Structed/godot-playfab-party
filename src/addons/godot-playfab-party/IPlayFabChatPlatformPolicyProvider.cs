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

using System;
using System.Collections.Generic;
using PartyCSharpSDK;

namespace PlayFab.Party._Internal
{
    /// <summary>
    /// The interface of a class that can provide an additional user- and platform-specific permission control 
    /// for Party voice and text messaging service, typically when it is needed by external ecosystem or application 
    /// requirements (e.g. Xbox Live)
    /// </summary>
    internal interface IPlayFabChatPlatformPolicyProvider
    {
        void SignIn();
        void SendPlatformSpecificUserId(List<PlayFabPlayer> targetPlayers);
        PARTY_VOICE_CHAT_TRANSCRIPTION_OPTIONS GetPlatformUserChatTranscriptionPreferences();
        bool IsTextToSpeechEnabled();
        PARTY_CHAT_PERMISSION_OPTIONS GetChatPermissions(PlayFabPlayer targetPlayer);
        void CreateOrUpdatePlatformUser(PlayFabPlayer player, bool isLocal);
        void ProcessEndpointMessage(PlayFabPlayer fromPlayer, IntPtr messageBuffer, uint messageSize, out bool isInternalMessage);

        void ProcessQueuedOperations();

        void ProcessStateChanges();
        bool CleanUp();
    }
}
