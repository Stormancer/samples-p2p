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
using Server.Plugins.API;
using Stormancer;
using Stormancer.Core;
using Stormancer.Platform.Core.Cryptography;
using Stormancer.Plugins;
using Stormancer.Server.Components;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    internal class UserSessionController : ControllerBase
    {
        private readonly ISceneHost _scene;
        private readonly ISerializer _serializer;
        private readonly IEnvironment _environment;
        private readonly UserSessions _sessions;

        //public Task<IScenePeerClient> GetPeer(string userId)
        //{
        //    throw new NotImplementedException();
        //}

        //public Task<User> GetUser(IScenePeerClient peer)
        //{
        //    throw new NotImplementedException();
        //}

        //public Task<bool> IsAuthenticated(IScenePeerClient peer)
        //{
        //    throw new NotImplementedException();
        //}

        //public Task UpdateUserData<T>(IScenePeerClient peer, T data)
        //{
        //    throw new NotImplementedException();
        //}


        public UserSessionController(UserSessions sessions, ISerializer serializer, ISceneHost scene, IEnvironment env)
        {
            _environment = env;
            _sessions = sessions;
            _serializer = serializer;
            _scene = scene;
        }
        public async Task GetPeer(RequestContext<IScenePeer> rq)
        {
            var userId = _serializer.Deserialize<string>(rq.InputStream);
            var result = await _sessions.GetPeer(userId);

            rq.SendValue(s => _serializer.Serialize(result?.SessionId, s));
        }

        

        public async Task IsAuthenticated(RequestContext<IScenePeer> rq)
        {
            var sessionId = _serializer.Deserialize<string>(rq.InputStream);
            var peer = _scene.RemotePeers.FirstOrDefault(p => p.SessionId == sessionId);
            var isAuthenticated = await _sessions.IsAuthenticated(peer);
            rq.SendValue(s => _serializer.Serialize(isAuthenticated, s));
        }

        public async Task UpdateUserData(RequestContext<IScenePeer> rq)
        {
            var sessionId = _serializer.Deserialize<string>(rq.InputStream);
            var peer = _scene.RemotePeers.FirstOrDefault(p => p.SessionId == sessionId);
            var data = _serializer.Deserialize<JObject>(rq.InputStream);

            await _sessions.UpdateUserData(peer, data);
        }

        public async Task GetPlatformId(RequestContext<IScenePeer> rq)
        {
            var userId = _serializer.Deserialize<string>(rq.InputStream);
            var platformId = await _sessions.GetPlatformId(userId);

            rq.SendValue(s => _serializer.Serialize(platformId, s));
        }

        public async Task GetSessionByUserId(RequestContext<IScenePeer> rq)
        {
            var userId = _serializer.Deserialize<string>(rq.InputStream);
            var session = await _sessions.GetSession(userId);

            rq.SendValue(s => _serializer.Serialize(session, s));
        }

        public async Task GetSessionById(RequestContext<IScenePeer> rq)
        {
            var peerId = _serializer.Deserialize<string>(rq.InputStream);
            var session = await _sessions.GetSessionById(peerId);

            rq.SendValue(s => _serializer.Serialize(session, s));
        }

        public async Task GetSessionByPlatformId(RequestContext<IScenePeer> rq)
        {
            var platformId = _serializer.Deserialize<PlatformId>(rq.InputStream);
            var session = await _sessions.GetSession(platformId);

            rq.SendValue(s => _serializer.Serialize(session, s));
        }

        public async Task UpdateSessionData(RequestContext<IScenePeer> rq)
        {
            var platformId = _serializer.Deserialize<PlatformId>(rq.InputStream);
            var key = _serializer.Deserialize<string>(rq.InputStream);
            var length = rq.InputStream.Length - rq.InputStream.Position;
            var data = new byte[length];
            rq.InputStream.Read(data, 0, (int)length);

            var session = await _sessions.GetSession(platformId);
            if(session == null)
            {
                throw new ClientException("NotFound");
            }
            session.SessionData[key] = data;
        }

        public async Task GetSessionData(RequestContext<IScenePeer> rq)
        {
            var platformId = _serializer.Deserialize<PlatformId>(rq.InputStream);
            var key = _serializer.Deserialize<string>(rq.InputStream);


            var session = await _sessions.GetSession(platformId);
            if (session == null)
            {
                throw new ClientException("NotFound");
            }
            byte[] value;
            if (session.SessionData.TryGetValue(key, out value))
            {
                rq.SendValue(s => s.Write(value, 0, value.Length));
            }
        }

        public async Task DecodeBearerToken(RequestContext<IScenePeer> rq)
        {
            var token = _serializer.Deserialize<string>(rq.InputStream);
            var app = await _environment.GetApplicationInfos();
            var data = TokenGenerator.DecodeToken<BearerTokenData>(token, app.PrimaryKey);
            rq.SendValue(s=> _serializer.Serialize(data,s));
        }

        [Api(ApiAccess.Scene2Scene, ApiType.Rpc)]
        public async Task<Session> GetSessionByBearerToken(string bearerToken)
        {
            var app = await _environment.GetApplicationInfos();
            var data = TokenGenerator.DecodeToken<BearerTokenData>(bearerToken, app.PrimaryKey);
            if (data == null)
            {
                throw new ClientException("Invalid Token");
            }
            return await _sessions.GetSessionById(data.SessionId);

           
        }

        [Api(ApiAccess.Scene2Scene, ApiType.Rpc)]
        public async Task<string> GetBearerToken(string sessionId)
        {
            var app = await _environment.GetApplicationInfos();
            var session = await _sessions.GetSessionById(sessionId);
            return TokenGenerator.CreateToken(new BearerTokenData { SessionId = sessionId, pid = session.platformId, userId = session.User.Id, IssuedOn = DateTime.UtcNow, ValidUntil = DateTime.UtcNow + TimeSpan.FromHours(1) }, app.PrimaryKey);
        }
    }
}
