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
using Stormancer.Core;
using Stormancer.Platform.Core.Cryptography;
using Stormancer.Plugins;
using Stormancer.Plugins.ServiceLocator;
using Stormancer.Server.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    internal class UserSessionsProxy : IUserSessions
    {
        private readonly ISceneHost _scene;
        private readonly ISerializer _serializer;
        private readonly IServiceLocator _locator;
        private readonly UserSessionCache cache;
        private readonly IEnvironment env;


        public UserSessionsProxy(ISceneHost scene, ISerializer serializer, IEnvironment env, IServiceLocator locator, UserSessionCache cache)
        {

            _scene = scene;
            _serializer = serializer;
            _locator = locator;
            this.cache = cache;
            this.env = env;
        }

        private async Task<Packet<IScenePeer>> AuthenticatorRpc(string targetSessionId, string route, Action<Stream> writer, string type = "")
        {

            var session = targetSessionId != null ? await this.cache.GetSessionBySessionId(targetSessionId, false, string.Empty, false) : null;
            string sceneId;
            if (session != null)
            {
                sceneId = session.AuthenticatorUrl;
            }
            else
            {
                sceneId = await _locator.GetSceneId("stormancer.authenticator" + (string.IsNullOrEmpty(type) ? "" : "-" + type), "");
            }
            return await AuthenticatorRpcWithSceneId(sceneId, route, writer);
        }

        private async Task<Packet<IScenePeer>> AuthenticatorRpcWithSceneId(string sceneId, string route, Action<Stream> writer)
        {
            var rpc = _scene.DependencyResolver.Resolve<RpcService>();

            return await rpc.Rpc(route, new MatchSceneFilter(sceneId), writer, PacketPriority.MEDIUM_PRIORITY).LastOrDefaultAsync();
        }

        private async Task<string> GetSessionIdForUser(string userId)
        {
            var response = await AuthenticatorRpc(null, "usersession.getpeer", s => _serializer.Serialize(userId, s));
            if (response != null)
            {
                using (response.Stream)
                {
                    return _serializer.Deserialize<string>(response.Stream);
                }
            }
            else
            {
                return null;
            }

        }
        public Task<IScenePeerClient> GetPeer(string userId)
        {
            return cache.GetPeerByUserId(userId, "");
        }

        public async Task<User> GetUser(IScenePeerClient peer)
        {
            var session = await GetSessionById(peer.SessionId, false);
            return session?.User;

        }


        public async Task<bool> IsAuthenticated(IScenePeerClient peer)
        {
            var response = await AuthenticatorRpc(peer.SessionId, "usersession.isauthenticated", s => _serializer.Serialize(peer.SessionId, s));

            var result = _serializer.Deserialize<bool>(response.Stream);
            response.Stream.Dispose();
            return result;
        }

        public async Task UpdateUserData<T>(IScenePeerClient peer, T data)
        {
            var response = await AuthenticatorRpc(peer.SessionId, "usersession.updateuserdata", s =>
               {
                   _serializer.Serialize(peer.SessionId, s);
                   _serializer.Serialize(JObject.FromObject(data), s);
               });

            response.Stream.Dispose();

        }

        public async Task<PlatformId> GetPlatformId(string userId)
        {
            var session = await cache.GetSessionByUserId(userId, true, "", false);
            if (session != null)
            {
                return session.platformId;
            }
            else
            {
                throw new InvalidOperationException("player not connected to the scene.");
            }

        }

        public Task<Session> GetSessionByUserId(string userId, bool forceRefresh)
        {
            return cache.GetSessionByUserId(userId, true, "", forceRefresh);
            //var response = await AuthenticatorRpc("usersession.getsessionbyuserid", s => _serializer.Serialize(userId, s));

            //var result = _serializer.Deserialize<Session>(response.Stream);
            //response.Stream.Dispose();
            //return result;
        }

        public Task<Session> GetSessionById(string sessionId, string authType, bool forceRefresh)
        {
            return cache.GetSessionBySessionId(sessionId, true, authType, forceRefresh);
            //var response = await AuthenticatorRpc("usersession.getsessionbyid", s => _serializer.Serialize(sessionId, s),authType);
            //if (response != null)
            //{
            //    using (response.Stream)
            //    {
            //        var result = _serializer.Deserialize<Session>(response.Stream);
            //        return result;
            //    }
            //}
            //else
            //{
            //    return null;
            //}
        }

        public Task<Session> GetSessionById(string sessionId, bool forceRefresh)
        {
            return GetSessionById(sessionId, "", forceRefresh);
        }

        public async Task<Session> GetSession(IScenePeerClient peer, bool forceRefresh)
        {
            return await GetSessionById(peer.SessionId, forceRefresh);
        }

        /// <summary>
        /// Get the player session from the active authenticator scene (returns null for players authenticated on an older deployment.
        /// </summary>
        /// <param name="platformId"></param>
        /// <returns></returns>
        public async Task<Session> GetSession(PlatformId platformId, bool forceRefresh)
        {
            var response = await AuthenticatorRpc(null, "usersession.getsessionbyplatformid", s => _serializer.Serialize(platformId, s));
            using (response.Stream)
            {
                var result = _serializer.Deserialize<Session>(response.Stream);

                return result;
            }
        }

        public async Task UpdateSessionData(string sessionId, string key, byte[] data)
        {
            var response = await AuthenticatorRpc(sessionId, "usersession.updatesessiondata", s =>
             {
                 _serializer.Serialize(sessionId, s);
                 _serializer.Serialize(key, s);
                 s.Write(data, 0, data.Length);
             });

            response?.Stream.Dispose();
        }

        public async Task<byte[]> GetSessionData(string sessionId, string key)
        {
            var response = await AuthenticatorRpc(sessionId, "usersession.getsessiondata", s =>
             {
                 _serializer.Serialize(sessionId, s);
                 _serializer.Serialize(key, s);
             });

            using (response.Stream)
            {
                if (response.Stream.Length > 0)
                {
                    var data = new byte[response.Stream.Length];
                    response.Stream.Read(data, 0, data.Length);
                    return data;
                }
                else
                {
                    return null;
                }
            }
        }

        public async Task UpdateSessionData<T>(string sessionId, string key, T data)
        {
            var response = await AuthenticatorRpc(sessionId, "usersession.updatesessiondata", s =>
             {
                 _serializer.Serialize(sessionId, s);
                 _serializer.Serialize(key, s);
                 _serializer.Serialize(data, s);
             });

            response?.Stream.Dispose();

            // Refresh the cache to get the new session data
            await GetSessionById(sessionId, true);
        }

        public async Task<T> GetSessionData<T>(string sessionId, string key)
        {
            var response = await AuthenticatorRpc(sessionId, "usersession.getsessiondata", s =>
             {
                 _serializer.Serialize(sessionId, s);
                 _serializer.Serialize(key, s);
             });

            using (response.Stream)
            {
                if (response.Stream.Length > 0)
                {
                    return _serializer.Deserialize<T>(response.Stream);
                }
                else
                {
                    return default(T);
                }
            }
        }

        public async Task<BearerTokenData> DecodeBearerToken(string token)
        {
            var app = await env.GetApplicationInfos();
            return TokenGenerator.DecodeToken<BearerTokenData>(token, app.PrimaryKey);

            //var response = await AuthenticatorRpc("usersession.decodebearertoken", s =>
            //{
            //    _serializer.Serialize(token, s);
            //});

            //using (response.Stream)
            //{
            //    if (response.Stream.Length > 0)
            //    {
            //        return _serializer.Deserialize<BearerTokenData>(response.Stream);
            //    }
            //    else
            //    {
            //        throw new InvalidOperationException("An unknown error occured while trying to decode a bearer token");
            //    }
            //}
        }

        public async Task<string> GetBearerToken(string sessionId)
        {

            var session = await GetSessionById(sessionId, false);
            return await GetBearerToken(session);
            /*var response = await AuthenticatorRpc($"UserSession.GetBearerToken", s =>
            {
                _serializer.Serialize(sessionId, s);
            });

            using (response.Stream)
            {
                if (response.Stream.Length > 0)
                {
                    return _serializer.Deserialize<string>(response.Stream);
                }
                else
                {
                    throw new InvalidOperationException("An unknown error occured while trying to decode a bearer token");
                }
            }*/
        }

        public async Task<string> GetBearerToken(Session session)
        {
            var app = await env.GetApplicationInfos();
            return TokenGenerator.CreateToken(new BearerTokenData { AuthenticatorUrl = session.AuthenticatorUrl, SessionId = session.SessionId, pid = session.platformId, userId = session.User.Id, IssuedOn = DateTime.UtcNow, ValidUntil = DateTime.UtcNow + TimeSpan.FromHours(1) }, app.PrimaryKey);
        }

        public async Task<Session> GetSessionByBearerToken(string token, bool forceRefresh)
        {
            if (!TokenGenerator.ExtractTokenData<BearerTokenData>(token, out var claims, out var error))
            {
                throw new ArgumentException(error);
            }

            var response = await AuthenticatorRpcWithSceneId(claims.AuthenticatorUrl, "UserSession.GetSessionByBearerToken", s =>
             {
                 _serializer.Serialize(token, s);
             });

            using (response.Stream)
            {
                if (response.Stream.Length > 0)
                {
                    return _serializer.Deserialize<Session>(response.Stream);
                }
                else
                {
                    throw new InvalidOperationException("An unknown error occured while trying to decode a bearer token");
                }
            }
        }

        public async Task<Dictionary<string, User>> GetUsers(params string[] userIds)
        {
            var response = await AuthenticatorRpc(null, $"UserSession.{nameof(GetUsers)}", s =>
             {
                 _serializer.Serialize(userIds, s);
             });

            using (response.Stream)
            {
                if (response.Stream.Length > 0)
                {
                    return _serializer.Deserialize<Dictionary<string, User>>(response.Stream);
                }
                else
                {
                    throw new InvalidOperationException("An unknown error occured while trying to decode a bearer token");
                }
            }
        }

        public async Task<IEnumerable<User>> Query(IEnumerable<KeyValuePair<string, string>> query, int take, int skip)
        {
            var response = await AuthenticatorRpc(null, $"UserSession.{nameof(Query)}", s =>
             {
                 _serializer.Serialize(query, s);
                 _serializer.Serialize(take, s);
                 _serializer.Serialize(skip, s);
             });

            using (response.Stream)
            {
                if (response.Stream.Length > 0)
                {
                    return _serializer.Deserialize<IEnumerable<User>>(response.Stream);
                }
                else
                {
                    throw new InvalidOperationException("An unknown error occured while trying to decode a bearer token");
                }
            }
        }

        public async Task UpdateUserHandle(string userId, string newHandle, bool appendHash)
        {
            var response = await AuthenticatorRpc(null, $"UserSession.{nameof(UpdateUserHandle)}", s =>
             {
                 _serializer.Serialize(userId, s);
                 _serializer.Serialize(newHandle, s);
                 _serializer.Serialize(appendHash, s);
             });
        }
    }
}