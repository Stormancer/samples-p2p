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
using Stormancer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{

    public interface IUserSessions
    {
        /// <summary>
        /// Gets the identity of a connected peer.
        /// </summary>
        /// <param name="peer"></param>
        /// <returns>An user instance, or null if the peer isn't authenticated.</returns>
        Task<User> GetUser(IScenePeerClient peer);
        /// <summary>
        /// Gets the peer that has been authenticated with the provided user id.
        /// </summary>
        /// <param name="userId"></param>
        /// <returns>A peer instance of null if no peer is currently authenticated with this identity.</returns>
        Task<IScenePeerClient> GetPeer(string userId);
        Task UpdateUserData<T>(IScenePeerClient peer, T data);

        Task<bool> IsAuthenticated(IScenePeerClient peer);

        Task<PlatformId> GetPlatformId(string userId);

        /// <summary>
        /// Gets a session by the user id (returns null if user isn't currently connected)
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        Task<Session> GetSessionByUserId(string userId);

        Task<Session> GetSession(IScenePeerClient peer);

        /// <summary>
        /// Gets a session by the session id of the peer
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        Task<Session> GetSessionById(string sessionId, string authType = "");

        Task<Session> GetSession(PlatformId platformId);

        Task UpdateSessionData<T>(PlatformId platformId, string key, T data);
        Task<T> GetSessionData<T>(PlatformId platformId, string key);
        Task<string> GetBearerToken(string sessionId);
        Task<BearerTokenData> DecodeBearerToken(string token);

        Task<Session> GetSessionByBearerToken(string token);
    }

    public class BearerTokenData
    {
        public string version { get; set; } = "2";
        public string SessionId { get; set; }
        public PlatformId pid { get; set; }
        public string userId { get; set; }
        public DateTime IssuedOn { get; set; }
        public DateTime ValidUntil { get; set; }
    }

    public struct PlatformId
    {
        public override string ToString()
        {
            return Platform + ":" + OnlineId;
        }
        public string Platform { get; set; }
        public string OnlineId { get; set; }

        public bool IsUnknown
        {
            get
            {
                return Platform == "unknown";
            }
        }

        public static PlatformId Unknown
        {
            get
            {
                return new PlatformId { Platform = "unknown", OnlineId = "" };
            }
        }


    }
}
