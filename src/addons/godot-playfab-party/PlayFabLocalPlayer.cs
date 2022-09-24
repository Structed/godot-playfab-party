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

namespace PlayFab.Party
{
    /// <summary>
    /// A class representing a local player in the network.
    /// </summary>
    public class PlayFabLocalPlayer : PlayFabPlayer
    {
        internal string _preferredLanguageCode;

        /// <summary>
        /// Ctor
        /// </summary>
        public PlayFabLocalPlayer()
        {
            _isLocal = true;
        }

        /// <summary>
        /// Gets the indication whether any public API that relies on a chat control associated with this player can be safely used.
        /// Player's chat control becomes available after the user creates or joins a party.
        /// </summary>
        public bool IsChatControlAvailable
        {
            get
            {
                return _chatControlHandle != null;
            }
        }

        /// <summary>
        /// Gets or sets the language code for the player.
        /// Currently, setting the language will only have effect before creating or joining a Party network.
        /// </summary>
        public string LanguageCode
        {
            get
            {
                PlayFabMultiplayerManager playFabMultiplayerManager = PlayFabMultiplayerManager.Get();
                return playFabMultiplayerManager._GetLanguageCode(EntityKey, true);
            }
            set
            {
                if (!IsChatControlAvailable)
                {
                    _preferredLanguageCode = value;
                }
            }
        }

        /// <summary>
        /// Gets an additional identifier that represents the platform's user-specific identifier.
        /// </summary>
        public string PlatformSpecificUserId
        {
            get
            {
                return _platformSpecificUserId;
            }
        }
    }
}
