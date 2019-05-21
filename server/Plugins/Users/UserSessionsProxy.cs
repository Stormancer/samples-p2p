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
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    internal class UserSessionsProxy : IUserSessions
    {
        private readonly ISceneHost _scene;
        private readonly ISerializer _serializer;
        private readonly IServiceLocator _locator;
        private readonly IEnvironment _env;

        public UserSessionsProxy(ISceneHost scene, ISerializer serializer, IEnvironment env, IServiceLocator locator)
        {
            _env = env;
            _scene = scene;
            _serializer = serializer;
            _locator = locator;
        }

        private async Task<Packet<IScenePeer>> AuthenticatorRpc(string route, Action<Stream> writer, string type = "")
        {
            var rpc = _scene.DependencyResolver.Resolve<RpcService>();
            return await rpc.Rpc(route, new MatchSceneFilter(await _locator.GetSceneId("stormancer.authenticator" + (string.IsNullOrEmpty(type) ? "" : "-" + type), "")), writer, PacketPriority.MEDIUM_PRIORITY).LastOrDefaultAsync();
        }
        public async Task<IScenePeerClient> GetPeer(string userId)
        {
            var response = await AuthenticatorRpc("usersession.getpeer", s => _serializer.Serialize(userId, s));

            var sessionId = _serializer.Deserialize<string>(response.Stream);
            var peer = _scene.RemotePeers.FirstOrDefault(p => p.SessionId == sessionId);
            response.Stream.Dispose();
            return peer;
        }

        public async Task<User> GetUser(IScenePeerClient peer)
        {
            var session = await GetSessionById(peer.SessionId);
            return session?.User;

        }

        public async Task<bool> IsAuthenticated(IScenePeerClient peer)
        {
            var response = await AuthenticatorRpc("usersession.isauthenticated", s => _serializer.Serialize(peer.SessionId, s));

            var result = _serializer.Deserialize<bool>(response.Stream);
            response.Stream.Dispose();
            return result;
        }

        public async Task UpdateUserData<T>(IScenePeerClient peer, T data)
        {
            var response = await AuthenticatorRpc("usersession.updateuserdata", s =>
              {
                  _serializer.Serialize(peer.SessionId, s);
                  _serializer.Serialize(JObject.FromObject(data), s);
              });

            response.Stream.Dispose();

        }

        public async Task<PlatformId> GetPlatformId(string userId)
        {
            var response = await AuthenticatorRpc("usersession.getplatformid", s => _serializer.Serialize(userId, s));

            var result = _serializer.Deserialize<PlatformId>(response.Stream);
            response.Stream.Dispose();
            return result;
        }

        public async Task<Session> GetSessionByUserId(string userId)
        {
            var response = await AuthenticatorRpc("usersession.getsessionbyuserid", s => _serializer.Serialize(userId, s));

            var result = _serializer.Deserialize<Session>(response.Stream);
            response.Stream.Dispose();
            return result;
        }

        public async Task<Session> GetSessionById(string sessionId, string authType = "")
        {
            var response = await AuthenticatorRpc("usersession.getsessionbyid", s => _serializer.Serialize(sessionId, s),authType);

            var result = _serializer.Deserialize<Session>(response.Stream);
            response.Stream.Dispose();
            return result;
        }
        public async Task<Session> GetSession(IScenePeerClient peer)
        {
            return await GetSessionById(peer.SessionId);
        }

        public async Task<Session> GetSession(PlatformId platformId)
        {
            var response = await AuthenticatorRpc("usersession.getsessionbyplatformid", s => _serializer.Serialize(platformId, s));
            using (response.Stream)
            {
                var result = _serializer.Deserialize<Session>(response.Stream);

                return result;
            }
        }

        public async Task UpdateSessionData<T>(PlatformId platformId, string key, T data)
        {
            var response = await AuthenticatorRpc("usersession.updatesessiondata", s =>
            {
                _serializer.Serialize(platformId, s);
                _serializer.Serialize(key, s);
                _serializer.Serialize(data, s);
            });

            response?.Stream.Dispose();

        }

        public async Task<T> GetSessionData<T>(PlatformId platformId, string key)
        {
            var response = await AuthenticatorRpc("usersession.getsessiondata", s =>
            {
                _serializer.Serialize(platformId, s);
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
            var response = await AuthenticatorRpc("usersession.decodebearertoken", s =>
            {
                _serializer.Serialize(token, s);
            });

            using (response.Stream)
            {
                if (response.Stream.Length > 0)
                {
                    return _serializer.Deserialize<BearerTokenData>(response.Stream);
                }
                else
                {
                    throw new InvalidOperationException("An unknown error occured while trying to decode a bearer token");
                }
            }
        }

        public async Task<string> GetBearerToken(string sessionId)
        {
            var response = await AuthenticatorRpc($"UserSession.GetBearerToken", s =>
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
            }
        }
        public async Task<Session> GetSessionByBearerToken(string token)
        {
            var response = await AuthenticatorRpc("UserSession.GetSessionByBearerToken", s =>
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
    }
}
