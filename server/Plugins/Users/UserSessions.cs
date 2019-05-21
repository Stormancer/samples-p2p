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
using Stormancer;
using Stormancer.Server.Database;
using Stormancer.Core;
using Stormancer.Diagnostics;

namespace Stormancer.Server.Users
{
    public interface IUserPeerIndex : IIndex<string> { }
    internal class UserPeerIndex : InMemoryIndex<string>, IUserPeerIndex { }

    public interface IPeerUserIndex : IIndex<Session> { }
    internal class PeerUserIndex : InMemoryIndex<Session>, IPeerUserIndex { }

    public class Session
    {
        public PlatformId platformId { get; set; }
        public User User { get; set; }
        public string SessionId { get; set; }

        public Dictionary<string, byte[]> SessionData { get; internal set; } = new Dictionary<string, byte[]>();
    }

    public class UserSessions
    {
        /// <summary>
        /// Contains correspondance from userId or online id to session id
        /// </summary>
        private readonly IUserPeerIndex _userSessionIndex;
        private readonly IUserService _userService;
        private readonly IPeerUserIndex _peerUserIndex;
        private readonly IEnumerable<IUserSessionEventHandler> _eventHandlers;
        private readonly IEnumerable<IAuthenticationProvider> _authProviders;
        private readonly ISceneHost _scene;
        private readonly ILogger logger;

        public UserSessions(IUserService userService,
            IPeerUserIndex peerUserIndex,
            IUserPeerIndex userPeerIndex,
            IEnumerable<IUserSessionEventHandler> eventHandlers,
            IEnumerable<IAuthenticationProvider> authProviders,
            ISceneHost scene, ILogger logger)
        {
            _userService = userService;
            _peerUserIndex = peerUserIndex;
            _userSessionIndex = userPeerIndex;
            _eventHandlers = eventHandlers;
            _scene = scene;
            _authProviders = authProviders;

            this.logger = logger;
        }

        public async Task<User> GetUser(IScenePeerClient peer)
        {
            var session = await GetSession(peer);

            return session?.User;
        }

        public async Task<bool> IsAuthenticated(IScenePeerClient peer)
        {
            return (await GetUser(peer)) != null;
        }

        public async Task<bool> LogOut(IScenePeerClient peer)
        {
            var sessionId = peer.SessionId;
            var session = await GetSessionById(sessionId);
            var result = await _peerUserIndex.TryRemove(sessionId);
            if (result.Success)
            {
                await _userSessionIndex.TryRemove(result.Value.User.Id);
                await _userSessionIndex.TryRemove(result.Value.platformId.ToString());
                var logoutContext = new LogoutContext { Session = session, ConnectedOn = DateTime.UtcNow };
                await _eventHandlers.RunEventHandler(h => h.OnLoggedOut(logoutContext), ex => logger.Log(LogLevel.Error, "usersessions", "An error occured while running LoggedOut event handlers", ex));

                //logger.Trace("usersessions", $"removed '{result.Value.Id}' (peer : '{peer.Id}') in scene '{_scene.Id}'.");
            }

            return result.Success;
        }

        private Task<bool> LogOut(string sessionId)
        {
            var peer = _scene.RemotePeers.FirstOrDefault(p => p.SessionId == sessionId);
            if (peer == null)
            {
                return Task.FromResult(false);
            }
            else
            {
                return LogOut(peer);
            }
        }

        public async Task Login(IScenePeerClient peer, User user, PlatformId onlineId, Dictionary<string, byte[]> sessionData)
        {
            if (peer == null)
            {
                throw new ArgumentNullException("peer");
            }
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            bool added = false;
            while (!added)
            {
                var r = await _userSessionIndex.GetOrAdd(user.Id, peer.SessionId);
                if (r.Value != peer.SessionId)
                {
                    if (!await LogOut(r.Value))
                    {
                        logger.Warn("usersessions", $"user {user.Id} was found in _userPeerIndex but could not be logged out properly.", new { userId = user.Id, oldSessionId = r.Value, newSessionId = peer.SessionId });

                        await _userSessionIndex.TryRemove(user.Id);
                        await _userSessionIndex.TryRemove(onlineId.ToString().ToString());
                    }
                }
                else
                {
                    added = true;
                }
            }

            await _userSessionIndex.TryAdd(onlineId.ToString(), peer.SessionId);
            var session = new Session { User = user, platformId = onlineId, SessionData = sessionData, SessionId = peer.SessionId };
            await _peerUserIndex.TryAdd(peer.SessionId, session);
            var loginContext = new LoginContext { Session = session, Client = peer };
            await _eventHandlers.RunEventHandler(h => h.OnLoggedIn(loginContext), ex => logger.Log(LogLevel.Error, "usersessions", "An error occured while running LoggedIn event handlers", ex));


        }

        public async Task UpdateUserData<T>(IScenePeerClient peer, T data)
        {
            var user = await GetUser(peer);
            if (user == null)
            {
                throw new InvalidOperationException("User not found.");
            }
            else
            {
                user.UserData = Newtonsoft.Json.Linq.JObject.FromObject(data);
                await _userService.UpdateUserData(user.Id, data);
            }
        }

        public async Task<IScenePeerClient> GetPeer(string userId)
        {
            var result = await _userSessionIndex.TryGet(userId);

            if (result.Success)
            {
                var peer = _scene.RemotePeers.FirstOrDefault(p => p.SessionId == result.Value);
                //logger.Trace("usersessions", $"found '{userId}' (peer : '{result.Value}', '{peer.Id}') in scene '{_scene.Id}'.");
                if (peer == null)
                {
                    //logger.Trace("usersessions", $"didn't found '{userId}' (peer : '{result.Value}') in scene '{_scene.Id}'.");
                }
                return peer;
            }
            else
            {
                //logger.Trace("usersessions", $"didn't found '{userId}' in userpeer index.");
                return null;
            }
        }
        public async Task<Session> GetSession(string userId)
        {
            var result = await _userSessionIndex.TryGet(userId);

            if (result.Success)
            {
                return await GetSessionById(result.Value);
            }
            else
            {
                return null;
            }
        }

        public async Task<PlatformId> GetPlatformId(string userId)
        {
            var session = await GetSession(userId);

            if (session != null)
            {
                return session.platformId;
            }

            return PlatformId.Unknown;
        }

        public Task<Session> GetSession(PlatformId id)
        {
            return GetSession(id.ToString());
        }

        public async Task<Session> GetSession(IScenePeerClient peer)
        {
            return peer != null ? await GetSessionById(peer.SessionId) : null;
        }

        public async Task<Session> GetSessionById(string sessionId)
        {
            var result = await _peerUserIndex.TryGet(sessionId);
            if (result.Success)
            {
                return result.Value;
            }
            else
            {
                return null;
            }
        }

        public int AuthenticatedUsersCount
        {
            get
            {
                return _peerUserIndex.Count;
            }
        }
    }
}
