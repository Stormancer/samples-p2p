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
using Server.Plugins.Configuration;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server.Analytics;
using Stormancer.Server.Components;
using Stormancer.Server.GameFinder.Models;
using Stormancer.Server.GameSession;
using Stormancer.Server.Users;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.GameFinder
{
    public class GameFinderContext
    {
        public List<Group> WaitingClient { get; set; } = new List<Group>();

        public List<(Group client, string reason)> FailedClients { get; set; } = new List<(Group client, string reason)>();

        public void SetFailed(Group group, string reason)
        {
            FailedClients.Add((group, reason));
        }
    }

    internal class GameFinderService : IGameFinderService
    {
        private const string UPDATE_NOTIFICATION_ROUTE = "gamefinder.update";
        private const string UPDATE_READYCHECK_ROUTE = "gamefinder.ready.update";
        private const string UPDATE_FINDGAME_REQUEST_PARAMS_ROUTE = "gamefinder.parameters.update";
        private const string LOG_CATEGORY = "GameFinderService";

        private ISceneHost _scene;

        private readonly IEnumerable<IGameFinderDataExtractor> _extractors;
        private readonly Func<IEnumerable<IGameFinderEventHandler>> handlers;
        private readonly IAnalyticsService analytics;
        private readonly IGameFinder _gameFinder;

        private readonly ILogger _logger;
        private readonly ISerializer _serializer;
        private readonly GameFinderData _data;

        // GameFinder Configuration
        public bool IsRunning { get => _data.IsRunning; private set => _data.IsRunning = value; }

        public GameFinderService(ISceneHost scene,
            IEnumerable<IGameFinderDataExtractor> extractors,
            Func<IEnumerable<IGameFinderEventHandler>> handlers,

            IAnalyticsService analytics,
            IEnvironment env,
            IGameFinder gameFinder,
            ILogger logger,
            IConfiguration config,
            ISerializer serializer,
            GameFinderData data)
        {
            _extractors = extractors;
            this.handlers = handlers;
            this.analytics = analytics;
            _gameFinder = gameFinder;
            
            _logger = logger;
            _serializer = serializer;
            _data = data;

            Init(scene);
            env.ActiveDeploymentChanged += Env_ActiveDeploymentChanged;
            config.SettingsChanged += (s, c) => ApplyConfig(c);
            ApplyConfig(config.Settings);

            scene.Disconnected.Add(args => CancelGame(args.Peer, false));
            scene.AddProcedure("gamefinder.find", FindGame);
            scene.AddRoute("gamefinder.ready.resolve", ResolveReadyRequest, r => r);
            scene.AddRoute("gamefinder.cancel", CancelGame, r => r);
        }

        private void Env_ActiveDeploymentChanged(object sender, ActiveDeploymentChangedEventArgs e)
        {
            if (!e.IsActive)
            {
                _data.acceptRequests = false;
                _ = CancelAll();
            }
        }

        private void ApplyConfig(dynamic config)
        {
            if (_data.kind == null || config == null)
            {
                _logger.Log(LogLevel.Error, LOG_CATEGORY, "GameFinder service can't find gameFinder kind or server application config", new { gameFinderKind = _data.kind });
                return;
            }

            var gameFinderConfigs = (JObject)config.gamefinder?.configs;
            dynamic specificConfig = gameFinderConfigs?.GetValue(_data.kind);

            _data.interval = TimeSpan.FromSeconds((double)(specificConfig?.interval ?? 1));
            _data.isReadyCheckEnabled = (bool?)specificConfig?.readyCheck?.enabled ?? false;
            _data.readyCheckTimeout = (int)(specificConfig?.readyCheck?.timeout ?? 1000);

            foreach (var extractor in _extractors)
            {
                extractor.RefreshConfig(specificConfig);
            }

            _gameFinder.RefreshConfig(specificConfig, config);

        }

        // This function called from GameFinder plugin
        public void Init(ISceneHost gameFinderScene)
        {
            _data.kind = gameFinderScene.Metadata[GameFinderPlugin.METADATA_KEY];

            _logger.Log(LogLevel.Trace, LOG_CATEGORY, "Initializing the GameFinderService.", new { extractors = _extractors.Select(e => e.GetType().ToString()) });

            if (this._scene != null)
            {
                throw new InvalidOperationException("The gameFinder service may only be initialized once.");
            }

            this._scene = gameFinderScene;
        }

        private class PeerInGroup
        {
            public IScenePeerClient Peer { get; set; }
            public Player Player { get; set; }
        }

        public async Task FindGameS2S(RequestContext<IScenePeer> requestS2S)
        {
            if (!_data.acceptRequests)
            {
                throw new ClientException("gamefinder.disabled?reason=deploymentNotActive");
            }
            var group = new Group();
            var provider = _serializer.Deserialize<string>(requestS2S.InputStream);

            try
            {
                foreach (var extractor in _extractors)
                {
                    if (await extractor.ExtractDataS2S(provider, requestS2S.InputStream, group))
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {
                await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) => s.WriteByte((byte)GameFinderStatusUpdate.Failed));
                throw;
            }

            PeerInGroup[] peersInGroup = null;
            using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
            {
                var sessions = scope.Resolve<IUserSessions>();
                peersInGroup = await Task.WhenAll(group.Players.Select(async p => new PeerInGroup { Peer = await sessions.GetPeer(p.UserId), Player = p }));
            }
            var state = new GameFinderRequestState(group);

            try
            {
                foreach (var p in peersInGroup)
                {
                    if (p.Peer == null)
                    {
                        throw new ClientException($"'{p.Player.UserId} has disconnected.");
                    }
                    //If player already waiting just replace infos instead of failing
                    //if (_data.peersToGroup.ContainsKey(p.Peer.Id))
                    //{
                    //    throw new ClientException($"'{p.Player.UserId} is already waiting for a game.");
                    //}
                }

                _data.waitingGroups[group] = state;
                foreach (var p in peersInGroup)
                {
                    _data.peersToGroup[p.Peer.SessionId] = group;
                }

                requestS2S.CancellationToken.Register(() =>
                {
                    state.Tcs.TrySetCanceled();
                });

                var memStream = new MemoryStream();
                requestS2S.InputStream.Seek(0, SeekOrigin.Begin);
                requestS2S.InputStream.CopyTo(memStream);
                await BroadcastToPlayers(group, UPDATE_FINDGAME_REQUEST_PARAMS_ROUTE, (s, sz) =>
                {
                    memStream.Seek(0, System.IO.SeekOrigin.Begin);
                    memStream.CopyTo(s);
                });
                await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) =>
                {
                    s.WriteByte((byte)GameFinderStatusUpdate.SearchStart);

                });
                state.State = RequestState.Ready;
            }
            catch (Exception ex)
            {
                state.Tcs.SetException(ex);
                await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) => s.WriteByte((byte)GameFinderStatusUpdate.Failed));

            }
            IGameResolverContext resolutionContext;

            try
            {
                resolutionContext = await state.Tcs.Task;
            }
            catch (TaskCanceledException)
            {
                await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) => s.WriteByte((byte)GameFinderStatusUpdate.Cancelled));
            }
            finally //Always remove group from list.
            {
                GameFinderRequestState _;
                foreach (var p in peersInGroup)
                {
                    Group grp1;
                    if (p?.Peer?.SessionId != null)
                    {
                        _data.peersToGroup.TryRemove(p.Peer.SessionId, out grp1);
                    }
                }
                _data.waitingGroups.TryRemove(group, out _);
                if (_.Candidate != null)
                {
                    if (_pendingReadyChecks.TryGetValue(_.Candidate.Id, out var rc))
                    {
                        if (!rc.RanToCompletion)
                        {
                            // Todo jojo What can i do with this ?
                            //rc.Cancel(currentUser.Id);
                        }
                    }
                }
            }
        }

        public async Task FindGame(RequestContext<IScenePeerClient> request)
        {
            if (!_data.acceptRequests)
            {
                throw new ClientException("gamefinder.disabled?reason=deploymentNotActive");
            }

            var group = new Group();
            var provider = request.ReadObject<string>();

            User currentUser = null;
            using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
            {
                var sessions = scope.Resolve<IUserSessions>();
                currentUser = await sessions.GetUser(request.RemotePeer);
            }
            foreach (var extractor in _extractors)
            {
                if (await extractor.ExtractData(provider, request, group))
                {
                    break;
                }
            }
            if (!group.Players.Any())
            {
                group.Players.Add(new Player(request.RemotePeer.SessionId, currentUser.Id) { });
            }

            PeerInGroup[] peersInGroup = null;
            using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
            {
                var sessions = scope.Resolve<IUserSessions>();
                peersInGroup = await Task.WhenAll(group.Players.Select(async p => new PeerInGroup { Peer = await sessions.GetPeer(p.UserId), Player = p }));
            }

            foreach (var p in peersInGroup)
            {
                if (p.Peer == null)
                {
                    throw new ClientException($"'{p.Player.UserId} has disconnected.");
                }
                if (_data.peersToGroup.ContainsKey(p.Peer.SessionId))
                {
                    throw new ClientException($"'{p.Player.UserId} is already waiting for a game.");
                }
            }

            var state = new GameFinderRequestState(group);

            _data.waitingGroups[group] = state;
            foreach (var p in peersInGroup)
            {
                _data.peersToGroup[p.Peer.SessionId] = group;
            }

            request.CancellationToken.Register(() =>
            {
                state.Tcs.TrySetCanceled();
            });

            var memStream = new MemoryStream();
            request.InputStream.Seek(0, SeekOrigin.Begin);
            request.InputStream.CopyTo(memStream);
            await BroadcastToPlayers(group, UPDATE_FINDGAME_REQUEST_PARAMS_ROUTE, (s, sz) =>
            {
                memStream.Seek(0, System.IO.SeekOrigin.Begin);
                memStream.CopyTo(s);
            });
            await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) =>
            {
                s.WriteByte((byte)GameFinderStatusUpdate.SearchStart);

            });
            state.State = RequestState.Ready;

            IGameResolverContext resolutionContext;
            try
            {
                resolutionContext = await state.Tcs.Task;
            }
            catch (TaskCanceledException)
            {
                await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) => s.WriteByte((byte)GameFinderStatusUpdate.Cancelled));

            }
            catch (ClientException ex)
            {
                await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) =>
                {
                    s.WriteByte((byte)GameFinderStatusUpdate.Failed);
                    sz.Serialize(ex.Message, s);
                });
            }
            finally //Always remove group from list.
            {
                GameFinderRequestState _;
                foreach (var p in peersInGroup)
                {
                    Group grp1;
                    _data.peersToGroup.TryRemove(p.Peer.SessionId, out grp1);
                }
                _data.waitingGroups.TryRemove(group, out _);
                if (_.Candidate != null)
                {
                    if (_pendingReadyChecks.TryGetValue(_.Candidate.Id, out var rc))
                    {
                        if (!rc.RanToCompletion)
                        {
                            rc.Cancel(currentUser.Id);
                        }
                    }
                }
            }
        }

        public async Task Run(CancellationToken ct)
        {
            IsRunning = true;

            var watch = new Stopwatch();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    watch.Restart();
                    await this.FindGamesOnce();
                    watch.Stop();

                    var playersNum = this._data.waitingGroups.Where(kvp => kvp.Value.State == RequestState.Ready).Sum(kvp => kvp.Key.Players.Count);
                    var groupsNum = this._data.waitingGroups.Where(kvp => kvp.Value.State == RequestState.Ready).Count();
                    //_logger.Log(LogLevel.Trace, $"{LOG_CATEGORY}.Run", $"A {_data.kind} pass was run for {playersNum} players and {groupsNum} groups", new
                    //{
                    //    Time = watch.Elapsed,
                    //    playersWaiting = playersNum,
                    //    groupsWaiting = groupsNum
                    //});
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, LOG_CATEGORY, "An error occurred while running a gameFinder.", e);
                }
                await Task.Delay(this._data.interval);
            }
            IsRunning = false;
        }

        private async Task FindGamesOnce()
        {
            using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
            {
                var waitingClients = _data.waitingGroups.Where(kvp => kvp.Value.State == RequestState.Ready).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                try
                {

                    foreach (var value in waitingClients.Values)
                    {
                        value.State = RequestState.Searching;
                        value.Candidate = null;
                    }

                    GameFinderContext mmCtx = new GameFinderContext();
                    mmCtx.WaitingClient.AddRange(waitingClients.Keys);

                    var games = await this._gameFinder.FindGames(mmCtx);

                    analytics.Push("gameFinder", "pass", JObject.FromObject(new
                    {
                        type = _data.kind,
                        playersWaiting = _data.waitingGroups.SelectMany(kvp => kvp.Key.Players).Count(),
                        groups = _data.waitingGroups.Count(),
                        customData = _gameFinder.ComputeDataAnalytics(mmCtx)
                    }));

                    if (games.Games.Any())
                    {
                        //_logger.Log(LogLevel.Debug, $"{LOG_CATEGORY}.FindGamesOnce", $"Prepare resolutions {waitingClients.Count} players for {matches.Matches.Count} matches.", new { waitingCount = waitingClients.Count });
                        await scope.Resolve<IGameFinderResolver>().PrepareGameResolution(games);
                    }

                    foreach (var game in games.Games)
                    {
                        foreach (var group in game.Teams.SelectMany(t => t.Groups)) //Set game found to prevent players from being gameed again
                        {
                            var state = waitingClients[group];
                            state.State = RequestState.Found;
                            state.Candidate = game;
                        }

                        //_logger.Log(LogLevel.Debug, $"{LOG_CATEGORY}.FindGamesOnce", $"Resolve game for {waitingClients.Count} players", new { waitingCount = waitingClients.Count, currentGame = game });
                        _ = ResolveGameFound(game, waitingClients); // Resolve game, but don't wait for completion.
                                                                    //_logger.Log(LogLevel.Debug, $"{LOG_CATEGORY}.FindGamesOnce", $"Resolve complete game for {waitingClients.Count} players", new { waitingCount = waitingClients.Count, currentGame = game });
                    }
                }
                finally
                {

                    foreach (var value in waitingClients.Values.Where(v => v.State == RequestState.Searching))
                    {
                        value.State = RequestState.Ready;
                    }
                }
            }
        }

        private async Task ResolveGameFound(Game game, Dictionary<Group, GameFinderRequestState> waitingClients)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
            {
                try
                {
                    var resolverCtx = new GameResolverContext(game);
                    await scope.Resolve<IGameFinderResolver>().ResolveGame(resolverCtx);

                    var ctx = new GameStartedContext();
                    ctx.GameFinderId = this._scene.Id;
                    ctx.Game = game;
                    await handlers().RunEventHandler(h => h.OnGameStarted(ctx), ex => { });

                    if (_data.isReadyCheckEnabled)
                    {
                        await BroadcastToPlayers(game, UPDATE_NOTIFICATION_ROUTE, (s, sz) =>
                        {

                            s.WriteByte((byte)GameFinderStatusUpdate.WaitingPlayersReady);

                        });

                        using (var gameReadyCheckState = CreateReadyCheck(game))
                        {
                            gameReadyCheckState.StateChanged += update =>
                            {
                                BroadcastToPlayers(game, UPDATE_READYCHECK_ROUTE, (s, sz) =>
                                {
                                    sz.Serialize(update, s);
                                });
                            };
                            var result = await gameReadyCheckState.WhenCompleteAsync();

                            if (!result.Success)
                            {
                                foreach (var group in result.UnreadyGroups)//Cancel gameFinder for timeouted groups
                                {
                                    GameFinderRequestState mrs;
                                    if (_data.waitingGroups.TryGetValue(group, out mrs))
                                    {
                                        mrs.Tcs.TrySetCanceled();
                                    }
                                }
                                foreach (var group in result.ReadyGroups)//Put ready groups back in queue.
                                {
                                    GameFinderRequestState mrs;
                                    if (_data.waitingGroups.TryGetValue(group, out mrs))
                                    {
                                        mrs.State = RequestState.Ready;
                                        await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) =>
                                        {
                                            s.WriteByte((byte)GameFinderStatusUpdate.SearchStart);
                                        });

                                    }
                                }
                                return; //stop here
                            }
                            else
                            {
                                //
                            }
                        }
                    }

                    foreach (var player in await GetPlayers(game.AllGroups.ToArray()))
                    {
                        try
                        {
                            using (var stream = new MemoryStream())
                            {
                                var writerContext = new GameFinderResolutionWriterContext(player.Serializer(), stream, player);
                                // Write the connection token first, if a scene was created by the resolver
                                if (!string.IsNullOrEmpty(resolverCtx.GameSceneId))
                                {

                                    var gameSessions = scope.Resolve<IGameSessions>();
                                    var token = await gameSessions.CreateConnectionToken(resolverCtx.GameSceneId, player.SessionId);
                                    writerContext.WriteObjectToStream(token);

                                }
                                else
                                {
                                    // Empty connection token, to avoid breaking deserialization client-side
                                    writerContext.WriteObjectToStream("");
                                }
                                if (resolverCtx.ResolutionAction != null)
                                {
                                    await resolverCtx.ResolutionAction(writerContext);
                                }
                                await _scene.Send(new MatchPeerFilter(player.SessionId), UPDATE_NOTIFICATION_ROUTE, s =>
                                {
                                    s.WriteByte((byte)GameFinderStatusUpdate.Success);
                                    stream.Seek(0, SeekOrigin.Begin);
                                    stream.CopyTo(s);
                                }
                                , PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Log(LogLevel.Error, "gamefinder", "An error occured while trying to resolve a game for a player", ex);
                            await _scene.Send(new MatchPeerFilter(player.SessionId), UPDATE_NOTIFICATION_ROUTE, s =>
                            {
                                s.WriteByte((byte)GameFinderStatusUpdate.Failed);
                            }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                        }
                    }

                    foreach (var group in game.AllGroups)
                    {
                        foreach (var player in group.Players)
                        {
                            var sectx = new SearchEndContext();
                            sectx.GameFinderId = this._scene.Id;
                            sectx.Group = group;
                            sectx.PassesCount = ((GameFinderGroupData)group.GroupData).PastGameFinderPasses;
                            sectx.Reason = SearchEndReason.Succeeded;
                            await handlers().RunEventHandler(h => h.OnEnd(sectx), ex => { });
                        }
                        var state = waitingClients[group];
                        state.Tcs.TrySetResult(resolverCtx);
                    }
                }
                catch (Exception)
                {
                    await BroadcastToPlayers(game, UPDATE_NOTIFICATION_ROUTE, (s, sz) => s.WriteByte((byte)GameFinderStatusUpdate.Failed));
                    throw;
                }
            }
        }

        private ConcurrentDictionary<string, GameReadyCheck> _pendingReadyChecks = new ConcurrentDictionary<string, GameReadyCheck>();

        private GameReadyCheck CreateReadyCheck(Game game)
        {
            var readyCheck = new GameReadyCheck(_data.readyCheckTimeout, () => CloseReadyCheck(game.Id), game);

            _pendingReadyChecks.TryAdd(game.Id, readyCheck);
            return readyCheck;
        }

        private void CloseReadyCheck(string id)
        {
            _pendingReadyChecks.TryRemove(id, out _);
        }

        private GameReadyCheck GetReadyCheck(IScenePeerClient peer)
        {
            if (_data.peersToGroup.TryGetValue(peer.SessionId, out var g))
            {
                var gameFinderRq = _data.waitingGroups[g];
                var gameCandidate = _data.waitingGroups[g].Candidate;
                if (gameCandidate == null)
                {
                    return null;
                }
                return GetReadyCheck(gameCandidate.Id);
            }
            return null;
        }

        private GameReadyCheck GetReadyCheck(string gameId)
        {
            GameReadyCheck check;
            if (_pendingReadyChecks.TryGetValue(gameId, out check))
            {
                return check;
            }
            else
            {
                return null;
            }
        }

        public async Task ResolveReadyRequest(Packet<IScenePeerClient> packet)
        {
            User user = null;
            using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
            {
                var sessions = scope.Resolve<IUserSessions>();
                user = await sessions.GetUser(packet.Connection);
            }

            if (user == null)//User not authenticated
            {
                return;
            }

            var accepts = packet.Stream.ReadByte() > 0;

            var check = GetReadyCheck(packet.Connection);
            if (check == null)
            {
                return;
            }
            if (!check.ContainsPlayer(user.Id))
            {
                return;
            }

            check.ResolvePlayer(user.Id, accepts);
        }

        public Task CancelGame(Packet<IScenePeerClient> packet)
        {
            return CancelGame(packet.Connection, true);
        }

        public Task CancelGame(IScenePeerClient peer, bool requestedByPlayer)
        {
            Group group;
            if (!_data.peersToGroup.TryGetValue(peer.SessionId, out group))
            {
                return Task.CompletedTask;
            }

            return Cancel(group, requestedByPlayer);
        }

        public async Task CancelAll()
        {
            var tasks = new List<Task>();
            foreach (var group in _data.peersToGroup.Values.ToArray())
            {
                tasks.Add(Cancel(group, false));
            }
            await Task.WhenAll(tasks);
        }

        public Task Cancel(Group group, bool requestedByPlayer)
        {
            GameFinderRequestState mmrs;
            if (!_data.waitingGroups.TryGetValue(group, out mmrs))
            {
                return Task.CompletedTask;
            }

            mmrs.Tcs.TrySetCanceled();

            var sectx = new SearchEndContext();
            sectx.GameFinderId = this._scene.Id;
            sectx.Group = group;
            sectx.PassesCount = ((GameFinderGroupData)group.GroupData).PastGameFinderPasses;
            sectx.Reason = requestedByPlayer ? SearchEndReason.Canceled : SearchEndReason.Disconnected;
            return handlers().RunEventHandler(h => h.OnEnd(sectx), ex => { });
        }

        private Task<IScenePeerClient> GetPlayer(Player member)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
            {
                var sessions = scope.Resolve<IUserSessions>();
                return sessions.GetPeer(member.UserId);
            }
        }

        private async Task<IEnumerable<IScenePeerClient>> GetPlayers(Group group)
        {
            return await Task.WhenAll(group.Players.Select(GetPlayer));
        }

        private async Task<IEnumerable<IScenePeerClient>> GetPlayers(params Group[] groups)
        {
            return await Task.WhenAll(groups.SelectMany(g => g.Players).Select(GetPlayer));
        }

        private Task BroadcastToPlayers(Game game, string route, Action<System.IO.Stream, ISerializer> writer)
        {
            return BroadcastToPlayers(game.Teams.SelectMany(t => t.Groups), route, writer);
        }

        private Task BroadcastToPlayers(Group group, string route, Action<System.IO.Stream, ISerializer> writer)
        {
            return BroadcastToPlayers(new Group[] { group }, route, writer);
        }

        private async Task BroadcastToPlayers(IEnumerable<Group> groups, string route, Action<System.IO.Stream, ISerializer> writer)
        {
            var peers = await GetPlayers(groups.ToArray());
            foreach (var group in peers.Where(p => p != null).GroupBy(p => p.Serializer()))
            {
                await _scene.Send(new MatchArrayFilter(group), route, s => writer(s, group.Key), PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
            }
        }

        public Dictionary<string, int> GetMetrics() => _gameFinder.GetMetrics();


        //private class GameFinderContext : IGameFinderContext
        //{
        //    private TaskCompletionSource<bool> _tcs;

        //    public GameFinderContext(RequestContext<IScenePeerClient> request, TaskCompletionSource<bool> tcs, Group data)
        //    {
        //        _tcs = tcs;
        //        Request = request;
        //        Group = data;
        //        CreationTimeUTC = DateTime.UtcNow;
        //    }

        //    public DateTime CreationTimeUTC { get; }

        //    public Group Group { get; set; }


        //    public bool Rejected { get; private set; }
        //    public object RejectionData { get; private set; }

        //    /// <summary>
        //    /// Write the data sent to all the player when gameFinder completes (success or failure)
        //    /// </summary>
        //    public Action<System.IO.Stream, ISerializer> ResolutionWriter { get; set; }
        //    public bool MatchFound { get; private set; }
        //    public object MatchFoundData { get; private set; }


        //    public RequestContext<IScenePeerClient> Request { get; }

        //    public void Fail(object failureData)
        //    {

        //        if (IsResolved)
        //        {
        //            throw new InvalidOperationException("This gameFinder context has already been resolved.");
        //        }
        //        Rejected = true;
        //        RejectionData = failureData;
        //        _tcs.SetResult(false);
        //    }

        //    public void Success(object successData)
        //    {
        //        if (IsResolved)
        //        {
        //            throw new InvalidOperationException("This gameFinder context has already been resolved.");
        //        }
        //        MatchFound = true;
        //        MatchFoundData = successData;
        //        _tcs.SetResult(true);
        //    }

        //    public bool IsResolved
        //    {
        //        get
        //        {
        //            return MatchFound || Rejected;
        //        }
        //    }
        //}

        private class GameResolverContext : IGameResolverContext
        {
            public GameResolverContext(Game game)
            {
                Game = game;
            }

            public Game Game { get; }

            public Func<IGameFinderResolutionWriterContext, Task> ResolutionAction { get; set; }

            public string GameSceneId { get; set; }
        }

        private class GameFinderResolutionWriterContext : IGameFinderResolutionWriterContext
        {
            private readonly Stream _stream;

            public GameFinderResolutionWriterContext(ISerializer serializer, Stream stream, IScenePeerClient peer)
            {
                Peer = peer;
                Serializer = serializer;
                _stream = stream;
            }

            public IScenePeerClient Peer { get; }
            public ISerializer Serializer { get; }

            public void WriteObjectToStream<T>(T data)
            {
                Serializer.Serialize(data, _stream);
            }

            public void WriteToStream(Action<Stream> writer)
            {
                writer(_stream);
            }
        }
    }
}
