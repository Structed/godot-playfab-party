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

namespace PlayFab.Party
{
    /// <summary>
    /// An event argument class representing a MultiplayerManager error.
    /// </summary>
    public class PlayFabMultiplayerManagerErrorArgs : EventArgs
    {
        /// <summary>
        /// ctor
        /// </summary>
        public PlayFabMultiplayerManagerErrorArgs()
        {
        }

        /// <summary>
        /// ctor
        /// </summary>
        public PlayFabMultiplayerManagerErrorArgs(int code, string message, PlayFabMultiplayerManagerErrorType type)
        {
            Code = code;
            Message = message;
            Type = type;
        }

        /// <summary>
        /// Gets the error code indicating the result of the operation.
        /// </summary>
        public int Code
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets a call-specific error message with debug information.
        /// This message is not localized as it is meant to be used for debugging only.
        /// </summary>
        public string Message
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets the type of the error that was raised from the operation.
        /// </summary>
        public PlayFabMultiplayerManagerErrorType Type
        {
            get;
            protected set;
        }
    }
}
