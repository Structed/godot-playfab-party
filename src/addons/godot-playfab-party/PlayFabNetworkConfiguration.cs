﻿/*
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

namespace PlayFab.Party
{
    /// <summary>
    /// A class containing information about the Party network.
    /// </summary>
    public class PlayFabNetworkConfiguration
    {
        private uint _maxPlayerCount;
        const uint _MAX_SUPPORTED_PLAYER_COUNT = PartyConstants.c_maxNetworkConfigurationMaxDeviceCount;

        private PARTY_DIRECT_PEER_CONNECTIVITY_OPTIONS _directPeerConnectivityOptions;

        private const string _ErrorMessageMaxUserCountValueOutOfRange = "Value must be between 1 and {0}";

        public PlayFabNetworkConfiguration()
        {
            _maxPlayerCount = _MAX_SUPPORTED_PLAYER_COUNT;
            // default is P2P
            _directPeerConnectivityOptions = PARTY_DIRECT_PEER_CONNECTIVITY_OPTIONS.PARTY_DIRECT_PEER_CONNECTIVITY_OPTIONS_ANY_PLATFORM_TYPE |
                                             PARTY_DIRECT_PEER_CONNECTIVITY_OPTIONS.PARTY_DIRECT_PEER_CONNECTIVITY_OPTIONS_ANY_ENTITY_LOGIN_PROVIDER;
        }

        /// <summary>
        /// Gets or sets the maximum number of players allowed to join the network.
        /// </summary>
        public uint MaxPlayerCount
        {
            get
            {
                return _maxPlayerCount;
            }
            set
            {
                if (value > 0 &&
                    value <= _MAX_SUPPORTED_PLAYER_COUNT)
                {
                    _maxPlayerCount = value;
                }
                else
                {
                    PlayFabMultiplayerManager._LogError(_ErrorMessageMaxUserCountValueOutOfRange.Replace("{0}", _MAX_SUPPORTED_PLAYER_COUNT.ToString()));
                }
            }
        }

        /// <summary>
        /// Gets or sets the Direct Peer Connectivity Options of the Network.
        /// </summary>
        public PARTY_DIRECT_PEER_CONNECTIVITY_OPTIONS DirectPeerConnectivityOptions
        {
            get
            {
                return _directPeerConnectivityOptions;
            }
            set
            {
                _directPeerConnectivityOptions = value;
            }
        }
    }
}
