// MIT License
//
// Copyright (c) 2019 Stormancer
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    public class AuthenticationResult
    {
        private AuthenticationResult()
        {
        }

        public static AuthenticationResult CreateSuccess(User user, PlatformId platformId, Dictionary<string, string> context)
        {
            return new AuthenticationResult { Success = true, AuthenticatedUser = user, PlatformId = platformId, AuthenticationContext = context };
        }

        public static AuthenticationResult CreateFailure(string reason, PlatformId platformId, Dictionary<string, string> context)
        {
            return new AuthenticationResult { Success = false, ReasonMsg = reason, PlatformId = platformId, AuthenticationContext = context };
        }

        public bool Success { get; private set; }

        public string AuthenticatedId
        {
            get
            {
                return AuthenticatedUser?.Id;
            }
        }

        public User AuthenticatedUser { get; private set; }

        public string ReasonMsg { get; private set; }

        public PlatformId PlatformId { get; private set; }

        public string Username
        {
            get
            {
                dynamic userData = AuthenticatedUser?.UserData;
                return (string)userData?.pseudo??"";
            }
        }

        public Dictionary<string, string> AuthenticationContext { get; private set; }

        public Dictionary<string, byte[]> initialSessionData { get; } = new Dictionary<string, byte[]>();

        /// <summary>
        /// The date at which this authentication should be renewed.
        /// </summary>
        /// <remarks>
        /// Leave it to null if this authentication doesn't need renewing.
        /// </remarks>
        public DateTime? ExpirationDate { get; set; } = null;

        public Action<SessionRecord> OnSessionUpdated;
    }
}

