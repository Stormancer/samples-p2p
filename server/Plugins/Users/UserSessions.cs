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

using Stormancer.Core;
using Stormancer.Core.Helpers;
using Stormancer.Diagnostics;
using Stormancer.Platform.Core.Cryptography;
using Stormancer.Server.Components;
using Stormancer.Server.Database;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    public interface IUserPeerIndex : IIndex<string> { }
    internal class UserPeerIndex : InMemoryIndex<string>, IUserPeerIndex { }

    public interface IPeerUserIndex : IIndex<SessionRecord> { }
    internal class PeerUserIndex : InMemoryIndex<SessionRecord>, IPeerUserIndex { }

    public class SessionRecord
    {
        public PlatformId platformId { get; set; }
        public User User { get; set; }
        public string SessionId { get; set; }
        public ConcurrentDictionary<string, string> Authentications { get; set; } = new ConcurrentDictionary<string, string>();
        public Dictionary<string, DateTime> AuthenticationExpirationDates { get; } = new Dictionary<string, DateTime>();
        public ConcurrentDictionary<string, byte[]> SessionData { get; internal set; } = new ConcurrentDictionary<string, byte[]>();
        public DateTime ConnectedOn { get; internal set; }

        /// <summary>
        /// absolute scene Url to the authenticator scene
        /// </summary>
        public string AuthenticatorUrl { get; set; }

        /// <summary>
        /// If the session is cached, the date at which it should expire
        /// </summary>
        public DateTimeOffset? MaxAge { get => AuthenticationExpirationDates.Count > 0 ? AuthenticationExpirationDates.Min().Value : (DateTimeOffset?)null; }

        public Session CreateView()
        {
            return new Session
            {
                platformId = platformId,
                User = User,
                SessionId = SessionId,
                SessionData = new Dictionary<string, byte[]>(SessionData),
                Authentications = new Dictionary<string, string>(Authentications),
                ConnectedOn = ConnectedOn,
                AuthenticatorUrl = AuthenticatorUrl,
                MaxAge = MaxAge
            };
        }
    }

    public class Session
    {
        public PlatformId platformId { get; set; }
        public User User { get; set; }
        public string SessionId { get; set; }
        public IReadOnlyDictionary<string, string> Authentications { get; set; }
        public IReadOnlyDictionary<string, byte[]> SessionData { get; internal set; }
        public DateTime ConnectedOn { get; internal set; }

        /// <summary>
        /// absolute scene Url to the authenticator scene
        /// </summary>
        public string AuthenticatorUrl { get; set; }

        /// <summary>
        /// If the session is cached, the date at which it should expire
        /// </summary>
        public DateTimeOffset? MaxAge { get; internal set; }
    }

    public class UserSessions : IUserSessions
    {
        private readonly IUserPeerIndex _userPeerIndex;
        private readonly IUserService _userService;
        private readonly IPeerUserIndex _peerUserIndex;
        private readonly Func<IEnumerable<IUserSessionEventHandler>> _eventHandlers;
        private readonly IESClientFactory _esClientFactory;
        private readonly ISerializer serializer;
        private readonly IEnvironment env;
        private readonly ISceneHost _scene;
        private readonly ILogger logger;

      
        public UserSessions(IUserService userService,
            IPeerUserIndex peerUserIndex,
            IUserPeerIndex userPeerIndex,
            Func<IEnumerable<IUserSessionEventHandler>> eventHandlers,
            ISerializer serializer,
            IESClientFactory eSClientFactory,
            IEnvironment env,
            ISceneHost scene, ILogger logger)
        {
            _esClientFactory = eSClientFactory;
            _userService = userService;
            _peerUserIndex = peerUserIndex;
            _userPeerIndex = userPeerIndex;
            _eventHandlers = eventHandlers;
            _scene = scene;

            this.serializer = serializer;
            this.env = env;
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
            var id = peer.SessionId;
            var session = await GetSessionById(sessionId);
            var result = await _peerUserIndex.TryRemove(sessionId);
            if (result.Success)
            {
                if (result.Value.User != null)
                {
                    await _userPeerIndex.TryRemove(result.Value.User.Id);
                }
                await _userPeerIndex.TryRemove(result.Value.platformId.ToString());
                var logoutContext = new LogoutContext { Session = session, ConnectedOn = session.ConnectedOn };
                await _eventHandlers().RunEventHandler(h => h.OnLoggedOut(logoutContext), ex => logger.Log(LogLevel.Error, "usersessions", "An error occured while running LoggedOut event handlers", ex));
                if (result.Value.User != null)
                {
                    await _userService.UpdateLastLoginDate(result.Value.User.Id);
                }
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


            bool added = false;

            while (!added && user != null)
            {
                var r = await _userPeerIndex.GetOrAdd(user.Id, peer.SessionId);
                if (r.Value != peer.SessionId)
                {
                    if (!await LogOut(peer))
                    {
                        logger.Warn("usersessions", $"user {user.Id} was found in _userPeerIndex but could not be logged out properly.", new { userId = user.Id, oldSessionId = r.Value, newSessionId = peer.SessionId });

                        await _userPeerIndex.TryRemove(user.Id);
                        await _userPeerIndex.TryRemove(onlineId.ToString().ToString());
                    }
                }
                else
                {
                    added = true;
                }
            }

            await _userPeerIndex.TryAdd(onlineId.ToString(), peer.SessionId);
            var session = new SessionRecord
            {
                User = user,
                platformId = onlineId,
                SessionData = new ConcurrentDictionary<string, byte[]>(sessionData.AsEnumerable()),
                SessionId = peer.SessionId,
                ConnectedOn = DateTime.UtcNow,
                AuthenticatorUrl = await GetAuthenticatorUrl()
            };

            await _peerUserIndex.AddOrUpdateWithRetries(peer.SessionId, session, s => { if (user != null) s.User = user; return s; });
            var loginContext = new LoginContext { Session = session, Client = peer };
            await _eventHandlers().RunEventHandler(h => h.OnLoggedIn(loginContext), ex => logger.Log(LogLevel.Error, "usersessions", "An error occured while running LoggedIn event handlers", ex));


        }

        private async Task<string> GetAuthenticatorUrl()
        {
            var infos = await env.GetApplicationInfos();
            // Support older grid versions where these values are null
            if (string.IsNullOrEmpty(infos.HostUrl) || string.IsNullOrEmpty(infos.ClusterId))
            {
                return _scene.Id;
            }
            return $"scene:/{infos.ClusterId}/{infos.HostUrl}/{_scene.Id}#{_scene.ShardId}";
        }

        internal Task UpdateSession(string id, Func<SessionRecord, Task<SessionRecord>> mutator)
        {
            return _peerUserIndex.UpdateWithRetries(id, mutator);
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
            var result = await _userPeerIndex.TryGet(userId);

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
        public async Task<Session> GetSession(string userId, bool forceRefresh = false)
        {
            var result = await _userPeerIndex.TryGet(userId);

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

        public Task<Session> GetSession(PlatformId id, bool forceRefresh = false)
        {
            return GetSession(id.ToString());
        }

        public async Task<Session> GetSession(IScenePeerClient peer, bool forceRefresh = false)
        {
            return peer != null ? await GetSessionById(peer.SessionId) : null;
        }


        public async Task<SessionRecord> GetSessionRecordById(string sessionId)
        {
            var result = await _peerUserIndex.TryGet(sessionId);
            if (result.Success)
            {
                return result.Value;
            }
            else
            {

                logger.Log(LogLevel.Trace, "usersession", $"Get session failed for id {sessionId}, {_peerUserIndex.Count} sessions found", new { });
                return null;
            }
        }

        public async Task<Session> GetSessionById(string sessionId, bool forceRefresh = false)
        {
            var session = await GetSessionRecordById(sessionId);
            return session?.CreateView();
        }

        public Task<Session> GetSessionByUserId(string userId, bool forceRefresh = false)
        {
            return GetSession(userId);
        }

        public Task<Session> GetSessionById(string sessionId, string authType, bool forceRefresh = false)
        {
            return GetSessionById(sessionId);
        }

        public async Task UpdateSessionData(string sessionId, string key, byte[] data)
        {
            var session = await GetSessionRecordById(sessionId);
            if (session == null)
            {
                throw new ClientException("session.notFound");
            }
            session.SessionData[key] = data;
        }

        public Task UpdateSessionData<T>(string sessionId, string key, T data)
        {
            var stream = new MemoryStream();
            serializer.Serialize(data, stream);
            return UpdateSessionData(sessionId, key, stream.ToArray());
        }

        public async Task<byte[]> GetSessionData(string sessionId, string key)
        {
            var session = await GetSessionById(sessionId);
            if (session == null)
            {
                throw new ClientException("NotFound");
            }
            if (session.SessionData.TryGetValue(key, out var value))
            {
                return value;
            }
            else
            {
                return null;
            }
        }

        public async Task<T> GetSessionData<T>(string sessionId, string key)
        {
            var data = await GetSessionData(sessionId, key);
            if (data != null)
            {
                using (var stream = new MemoryStream(data))
                {
                    return serializer.Deserialize<T>(stream);
                }
            }
            else
            {
                return default(T);
            }
        }

        public async Task<string> GetBearerToken(string sessionId)
        {

            var session = await GetSessionById(sessionId);
            return await GetBearerToken(session);
        }

        public async Task<string> GetBearerToken(Session session)
        {
            var app = await env.GetApplicationInfos();
            return TokenGenerator.CreateToken(new BearerTokenData { SessionId = session.SessionId, pid = session.platformId, userId = session.User.Id, IssuedOn = DateTime.UtcNow, ValidUntil = DateTime.UtcNow + TimeSpan.FromHours(1) }, app.PrimaryKey);
        }

        public async Task<BearerTokenData> DecodeBearerToken(string token)
        {
            var app = await env.GetApplicationInfos();
            return TokenGenerator.DecodeToken<BearerTokenData>(token, app.PrimaryKey);
        }

        public async Task<Session> GetSessionByBearerToken(string token, bool forceRefresh = false)
        {
            var data = await DecodeBearerToken(token);
            return await GetSessionById(data.SessionId);
        }

        public Task<Dictionary<string, User>> GetUsers(params string[] userIds)
        {
            return _userService.GetUsers(userIds);
        }

        public Task<IEnumerable<User>> Query(IEnumerable<KeyValuePair<string, string>> query, int take, int skip)
        {
            return _userService.Query(query, take, skip);
        }

        private static int _randomTracker = 0;

        private static ThreadLocal<Random> _random = new ThreadLocal<Random>(() =>
        {
            var seed = (int)(Environment.TickCount & 0xFFFFFF00 | (byte)(Interlocked.Increment(ref _randomTracker) % 255));
            var random = new Random(seed);
            return random;
        });

        private static bool _handleUserMappingCreated = false;
        private static AsyncLock _mappingLock = new AsyncLock();

        private const string UserHandleKey = "handle";

        private int _handleSuffixUpperBound = 10000;
        private int _handleMaxNumCharacters = 32;

        private async Task EnsureHandleUserMappingCreated()
        {
            if (!_handleUserMappingCreated)
            {
                using (await _mappingLock.LockAsync())
                {
                    if (!_handleUserMappingCreated)
                    {
                        _handleUserMappingCreated = true;
                        await _esClientFactory.EnsureMappingCreated<HandleUserRelation>("handleUserMapping", m => m
                            .Properties(pd => pd
                                .Keyword(kpd => kpd.Name(record => record.Id).Index())
                                .Keyword(kpd => kpd.Name(record => record.HandleWithoutNum).Index())
                                .Number(npd => npd.Name(record => record.HandleNum).Type(Nest.NumberType.Integer).Index())
                                .Keyword(kpd => kpd.Name(record => record.UserId).Index(false))
                                ));
                    }
                }
            }
        }

        public async Task UpdateUserHandle(string userId, string newHandle, bool appendHash)
        {
            // Check handle validity
            if (!Regex.IsMatch(newHandle, @"^[\p{Ll}\p{Lu}\p{Lt}\p{Lo}0-9]*$"))
            {
                throw new ArgumentException("Handle must consist of letters and digits only", nameof(newHandle));
            }
            if (newHandle.Length > _handleMaxNumCharacters)
            {
                throw new ArgumentException("Handle too long", nameof(newHandle));
            }
            await EnsureHandleUserMappingCreated();
            var client = await _esClientFactory.CreateClient<HandleUserRelation>("handleUserRelationClient");
            var user = await _userService.GetUser(userId);
            if (user == null)
            {
                throw new ClientException("notFound?user");
            }
            var newUserData = user.UserData;

            bool foundUnusedHandle = false;
            string newHandleWithSuffix;
            if (appendHash)
            {
                do
                {
                    var suffix = _random.Value.Next(0, _handleSuffixUpperBound);
                    newHandleWithSuffix = newHandle + "#" + suffix;

                    // Check conflicts
                    var relation = new HandleUserRelation { Id = newHandleWithSuffix, HandleNum = suffix, HandleWithoutNum = newHandle, UserId = userId };
                    var response = await client.IndexAsync(relation, d => d.OpType(Elasticsearch.Net.OpType.Create));
                    foundUnusedHandle = response.IsValid;

                } while (!foundUnusedHandle);
                newUserData[UserHandleKey] = newHandleWithSuffix;
            }
            else
            {
                newUserData[UserHandleKey] = newHandle;
            }


            await _userService.UpdateUserData(userId, newUserData);
        }

        public int AuthenticatedUsersCount
        {
            get
            {
                return _peerUserIndex.Count;
            }
        }
    }

    public class HandleUserRelation
    {
        /// <summary>
        /// Indexed by user's handle
        /// </summary>       
        public string Id { get; set; }

        public string HandleWithoutNum { get; set; }

        public int HandleNum { get; set; }

        public string UserId { get; set; }
    }
}
