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

using Newtonsoft.Json.Linq;
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
        /// Gets a session by the user id (returns null if user isn't currently connected to the scene)
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        Task<Session> GetSessionByUserId(string userId, bool forceRefresh = false);

        Task<Session> GetSession(IScenePeerClient peer, bool forceRefresh = false);

        /// <summary>
        /// Gets a session by the session id of the peer (returns null if the user isn't currently connected to the scene)
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        Task<Session> GetSessionById(string sessionId, bool forceRefresh = false);

        Task<Session> GetSessionById(string sessionId, string authType, bool forceRefresh = false);

        Task<Session> GetSession(PlatformId platformId, bool forceRefresh = false);

        /// <summary>
        /// Update a user's session data entry with raw data.
        /// </summary>
        /// <remarks>If <paramref name="key"/> is not present in the session data, it will be added.</remarks>
        /// <param name="sessionId">Id of the session</param>
        /// <param name="key">Session data key</param>
        /// <param name="data">Raw data to be set for <paramref name="key"/></param>
        /// <returns></returns>
        Task UpdateSessionData(string sessionId, string key, byte[] data);
        /// <summary>
        /// Retrieve a user's raw session data entry.
        /// </summary>
        /// <param name="sessionId">Id of the session</param>
        /// <param name="key">Key of the data to be retrieved</param>
        /// <returns>The raw data for <paramref name="key"/>, or <c>null</c> if <paramref name="key"/> does not exist.</returns>
        Task<byte[]> GetSessionData(string sessionId, string key);
        /// <summary>
        /// Update a user's session data entry with an object.
        /// </summary>
        /// <remarks>The object is serialized using the server's serializer.</remarks>
        /// <typeparam name="T">Type of the object to use as session data</typeparam>
        /// <param name="sessionId">Id of the session</param>
        /// <param name="key">Session data key</param>
        /// <param name="data">Object to store as session data for <paramref name="key"/></param>
        /// <returns></returns>
        Task UpdateSessionData<T>(string sessionId, string key, T data);
        /// <summary>
        /// Retrieve a user's session data entry, deserialized into a specific type.
        /// </summary>
        /// <typeparam name="T">Type of the data to be retrieved</typeparam>
        /// <param name="sessionId">Id of the session</param>
        /// <param name="key">Key of the data to be retrieved</param>
        /// <returns>The deserialized object at <paramref name="key"/>, or the default value for <typeparamref name="T"/> of <paramref name="key"/> doesn't exist.</returns>
        Task<T> GetSessionData<T>(string sessionId, string key);

        Task<string> GetBearerToken(string sessionId);
        Task<string> GetBearerToken(Session sessionId);

        Task<BearerTokenData> DecodeBearerToken(string token);

        Task<Session> GetSessionByBearerToken(string token, bool forceRefresh = false);

        Task<Dictionary<string, User>> GetUsers(params string[] userIds);

        Task<IEnumerable<User>> Query(IEnumerable<KeyValuePair<string,string>> query, int take, int skip);

        /// <summary>
        /// Updates the user handle, and optionally adds an hash number (ie name#2424) to make ids unique.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="newHandle"></param>
        /// <param name="appendHash"></param>
        /// <returns></returns>
        Task UpdateUserHandle(string userId, string newHandle, bool appendHash);
    }

    public class BearerTokenData
    {
        public string version { get; set; } = "2";
        public string SessionId { get; set; }
        public PlatformId pid { get; set; }
        public string userId { get; set; }
        public DateTime IssuedOn { get; set; }
        public DateTime ValidUntil { get; set; }
        public string AuthenticatorUrl { get; set; }
    }

    public struct PlatformId
    {
        public override string ToString()
        {
            return Platform + ":" + OnlineId;
        }
        public static PlatformId Parse(string value)
        {
            var els = value.Split(':');
            return new PlatformId { Platform = els[0], OnlineId = els[1] };
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

