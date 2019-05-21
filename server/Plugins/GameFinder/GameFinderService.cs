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

    public class GameFinderService : IGameFinderService
    {
        private const string UPDATE_NOTIFICATION_ROUTE = "gamefinder.update";
        private const string UPDATE_READYCHECK_ROUTE = "gamefinder.ready.update";
        private const string UPDATE_FINDGAME_REQUEST_PARAMS_ROUTE = "gamefinder.parameters.update";
        private const string LOG_CATEGORY = "GameFinderService";

        private ISceneHost _scene;

        private readonly IEnumerable<IGameFinderDataExtractor> _extractors;
        private readonly IGameFinder _gameFinder;
        private readonly IGameFinderResolver _resolver;
        private readonly ILogger _logger;
        private readonly IUserSessions _sessions;
        private readonly ISerializer _serializer;

        // GameFinder data
        private readonly ConcurrentDictionary<Group, GameFinderRequestState> _waitingGroups = new ConcurrentDictionary<Group, GameFinderRequestState>();
        private readonly ConcurrentDictionary<string, Group> _peersToGroup = new ConcurrentDictionary<string, Group>();

        // GameFinder Configuration
        public bool IsRunning { get; private set; }
        private string _kind;
        private TimeSpan _interval;
        private bool _isReadyCheckEnabled { get; set; }
        private int _readyCheckTimeout { get; set; }

        public GameFinderService(ISceneHost scene,
            IEnumerable<IGameFinderDataExtractor> extractors,
            IGameFinder gameFinder,
            IGameFinderResolver resolver,
            IUserSessions sessions,
            ILogger logger,
            IConfiguration config,
            ISerializer serializer)
        {
            _extractors = extractors;
            _gameFinder = gameFinder;
            _resolver = resolver;
            _sessions = sessions;
            _logger = logger;
            _serializer = serializer;

            Init(scene);

            config.SettingsChanged += (s, c) => ApplyConfig(c);
            ApplyConfig(config.Settings);

            scene.Disconnected.Add(args => CancelGame(args.Peer));
            scene.AddProcedure("gamefinder.find", FindGame);
            scene.AddRoute("gamefinder.ready.resolve", ResolveReadyRequest, r => r);
            scene.AddRoute("gamefinder.cancel", CancelGame, r => r);
        }

        private void ApplyConfig(dynamic config)
        {
            if (_kind == null || config == null)
            {
                _logger.Log(LogLevel.Error, LOG_CATEGORY, "GameFinder service can't find gameFinder kind or server application config", new { gameFinderKind = _kind });
                return;
            }

            var gameFinderConfigs = (JObject)config.gamefinder?.configs;

            dynamic specificConfig = gameFinderConfigs?.GetValue(_kind);

            _interval = TimeSpan.FromSeconds((double)(specificConfig?.interval ?? 1));
            _isReadyCheckEnabled = (bool?)specificConfig?.readyCheck?.enabled ?? false;
            _readyCheckTimeout = (int)(specificConfig?.readyCheck?.timeout ?? 1000);

            foreach (var extractor in _extractors)
            {
                extractor.RefreshConfig(specificConfig);
            }

            _gameFinder.RefreshConfig(specificConfig, config);
            _resolver.RefreshConfig(specificConfig);
        }

        // This function called from GameFinder plugin
        public void Init(ISceneHost gameFinderScene)
        {
            _kind = gameFinderScene.Metadata[GameFinderPlugin.METADATA_KEY];

            _logger.Log(LogLevel.Trace, LOG_CATEGORY, "Initializing the GameFinderService.", new { extractors = _extractors.Select(e => e.GetType().ToString()) });

            if (this._scene != null)
            {
                throw new InvalidOperationException("The gameFinder service may only be initialized once.");
            }

            this._scene = gameFinderScene;
        }

        public async Task FindGameS2S(RequestContext<IScenePeer> requestS2S)
        {
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
            var peersInGroup = await Task.WhenAll(group.Players.Select(async p => new { Peer = await _sessions.GetPeer(p.UserId), Player = p }));
            var state = new GameFinderRequestState(group);

            try
            {
                foreach (var p in peersInGroup)
                {
                    if (p.Peer == null)
                    {
                        throw new ClientException($"'{p.Player.UserId} has disconnected.");
                    }
                    //If player already waitingm just replace infos instead of failing
                    //if (_peersToGroup.ContainsKey(p.Peer.Id))
                    //{
                    //    throw new ClientException($"'{p.Player.UserId} is already waiting for a game.");
                    //}
                }

                _waitingGroups[group] = state;
                foreach (var p in peersInGroup)
                {
                    _peersToGroup[p.Peer.SessionId] = group;
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
                    _peersToGroup.TryRemove(p.Peer.SessionId, out grp1);
                }
                _waitingGroups.TryRemove(group, out _);
                if (_.Candidate != null)
                {
                    GameReadyCheck rc;

                    if (_pendingReadyChecks.TryGetValue(_.Candidate.Id, out rc))
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
            var group = new Group();
            var provider = request.ReadObject<string>();

            var currentUser = await _sessions.GetUser(request.RemotePeer);
            
            foreach (var extractor in _extractors)
            {
                if (await extractor.ExtractData(provider, request, group))
                {
                    break;
                }
            }
            if(!group.Players.Any())
            {
                group.Players.Add(new Player(currentUser.Id) { });
            }
            var peersInGroup = await Task.WhenAll(group.Players.Select(async p => new { Peer = await _sessions.GetPeer(p.UserId), Player = p }));

            foreach (var p in peersInGroup)
            {
                if (p.Peer == null)
                {
                    throw new ClientException($"'{p.Player.UserId} has disconnected.");
                }
                if (_peersToGroup.ContainsKey(p.Peer.SessionId))
                {
                    throw new ClientException($"'{p.Player.UserId} is already waiting for a game.");
                }
            }

            var state = new GameFinderRequestState(group);

            _waitingGroups[group] = state;
            foreach (var p in peersInGroup)
            {
                _peersToGroup[p.Peer.SessionId] = group;
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
                    _peersToGroup.TryRemove(p.Peer.SessionId, out grp1);
                }
                _waitingGroups.TryRemove(group, out _);
                if (_.Candidate != null)
                {
                    GameReadyCheck rc;

                    if (_pendingReadyChecks.TryGetValue(_.Candidate.Id, out rc))
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
                    await this.GameOnce();
                    watch.Stop();

                    /*_logger.Log(LogLevel.Trace, $"{LOG_CATEGORY}.Run", $"A gameFinder pass was run in {watch.Elapsed.TotalMilliseconds:F0}ms", new
                    {
                        Time = watch.Elapsed,
                        playersWaiting = this._waitingGroups.Where(kvp => kvp.Value.State == RequestState.Ready).Sum(kvp => kvp.Key.Players.Count),
                        groupsWaiting = this._waitingGroups.Where(kvp => kvp.Value.State == RequestState.Ready).Count()
                    });*/
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, LOG_CATEGORY, "An error occurred while running a gameFinder.", e);
                }
                await Task.Delay(this._interval);
            }
            IsRunning = false;
        }

        private async Task GameOnce()
        {
            var waitingClients = _waitingGroups.Where(kvp => kvp.Value.State == RequestState.Ready).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            try
            {
                foreach (var value in waitingClients.Values)
                {
                    value.State = RequestState.Searching;
                    value.Candidate = null;
                }

                GameFinderContext mmCtx = new GameFinderContext();
                mmCtx.WaitingClient.AddRange(waitingClients.Keys);

                //_logger.Log(LogLevel.Debug, $"{LOG_CATEGORY}.Run", $"Starting a {this._kind} pass for {waitingClients.Keys.Sum(d => d.Players.Count)} players.", new { playersWaiting = _waitingGroups.SelectMany(kvp => kvp.Key.Players).Count() });

                var games = await this._gameFinder.FindGames(mmCtx);
                //_logger.Log(LogLevel.Debug, $"{LOG_CATEGORY}.GameOnce", $"Finished a gameing pass for {waitingClients.Count} players, found {gamees.Gamees.Count} gamees.", new { waitingCount = _waitingGroups.Count, FoundGamees = gamees.Gamees.Count});

                foreach ((Group group, string reason) in mmCtx.FailedClients)
                {
                    var client = waitingClients[group];
                    client.State = RequestState.Rejected;
                    client.Tcs.TrySetException(new ClientException(reason));
                    // Remove gamees that contain a rejected group
                    games.Games.RemoveAll(m => m.AllGroups.Contains(group));
                }

                if (games.Games.Any())
                {
                    //_logger.Log(LogLevel.Debug, $"{LOG_CATEGORY}.GameOnce", $"Prepare resolutions {waitingClients.Count} players for {gamees.Gamees.Count} gamees.", new { waitingCount = waitingClients.Count });
                    await _resolver.PrepareGameResolution(games);
                }

                foreach (var game in games.Games)
                {
                    foreach (var group in game.Teams.SelectMany(t => t.Groups)) //Set game found to prevent players from being gameed again
                    {
                        var state = waitingClients[group];
                        state.State = RequestState.Found;
                        state.Candidate = game;
                    }

                    //_logger.Log(LogLevel.Debug, $"{LOG_CATEGORY}.GameOnce", $"Resolve game for {waitingClients.Count} players", new { waitingCount = waitingClients.Count, currentGame = game });
                    var _ = ResolveGameFound(game, waitingClients); // Resolve game, but don't wait for completion.
                    //_logger.Log(LogLevel.Debug, $"{LOG_CATEGORY}.GameOnce", $"Resolve complete game for {waitingClients.Count} players", new { waitingCount = waitingClients.Count, currentGame = game });
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

        private async Task ResolveGameFound(Game game, Dictionary<Group, GameFinderRequestState> waitingClients)
        {
            var resolverCtx = new GameResolverContext(game);
            await _resolver.ResolveGame(resolverCtx);

            if (_isReadyCheckEnabled)
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
                            if (_waitingGroups.TryGetValue(group, out mrs))
                            {
                                mrs.Tcs.TrySetCanceled();
                            }
                        }
                        foreach (var group in result.ReadyGroups)//Put ready groups back in queue.
                        {
                            GameFinderRequestState mrs;
                            if (_waitingGroups.TryGetValue(group, out mrs))
                            {
                                mrs.State = RequestState.Ready;
                                await BroadcastToPlayers(group, UPDATE_NOTIFICATION_ROUTE, (s, sz) =>
                                {
                                    s.WriteByte((byte)GameFinderStatusUpdate.SearchStart);
                                });

                            }
                        }
                        return; // stop here
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

            foreach (var group in game.Teams.SelectMany(t => t.Groups))//Complete requests
            {
                var state = waitingClients[group];
                state.Tcs.TrySetResult(resolverCtx);
            }
        }

        private ConcurrentDictionary<string, GameReadyCheck> _pendingReadyChecks = new ConcurrentDictionary<string, GameReadyCheck>();

        private GameReadyCheck CreateReadyCheck(Game game)
        {
            var readyCheck = new GameReadyCheck(_readyCheckTimeout, () => CloseReadyCheck(game.Id), game);

            _pendingReadyChecks.TryAdd(game.Id, readyCheck);
            return readyCheck;
        }

        private void CloseReadyCheck(string id)
        {
            GameReadyCheck _;
            _pendingReadyChecks.TryRemove(id, out _);
        }

        private GameReadyCheck GetReadyCheck(IScenePeerClient peer)
        {
            Group g;
            if (_peersToGroup.TryGetValue(peer.SessionId, out g))
            {
                var gameFinderRq = _waitingGroups[g];
                var gameCandidate = _waitingGroups[g].Candidate;
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

            var user = await _sessions.GetUser(packet.Connection);
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

        public async Task CancelGame(Packet<IScenePeerClient> packet)
        {
            await CancelGame(packet.Connection);
        }

        public Task CancelGame(IScenePeerClient peer)
        {
            Group group;
            if (!_peersToGroup.TryGetValue(peer.SessionId, out group))
            {
                return Task.CompletedTask;
            }

            GameFinderRequestState mmrs;
            if (!_waitingGroups.TryGetValue(group, out mmrs))
            {
                return Task.CompletedTask;
            }

            mmrs.Tcs.TrySetCanceled();

            return Task.CompletedTask;
        }

        private Task<IScenePeerClient> GetPlayer(Player member)
        {
            return _sessions.GetPeer(member.UserId);
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
        //    public bool GameFound { get; private set; }
        //    public object GameFoundData { get; private set; }


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
        //        GameFound = true;
        //        GameFoundData = successData;
        //        _tcs.SetResult(true);
        //    }

        //    public bool IsResolved
        //    {
        //        get
        //        {
        //            return GameFound || Rejected;
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
        }

        private class GameFinderResolutionWriterContext : IGameFinderResolutionWriterContext
        {
            private readonly Stream _stream;

            public GameFinderResolutionWriterContext(ISerializer serializer, Stream stream, IScenePeerClient peer)
            {

                Serializer = serializer;
                _stream = stream;
                Peer = peer;
            }

            public ISerializer Serializer { get; }
            public IScenePeerClient Peer { get; }

            public void WriteObjectToStream<T>(T data)
            {
                Serializer.Serialize(data, _stream);
            }

            public void WriteToStream(Action<Stream> writer)
            {
                writer(_stream);
            }
        }

        private class GameFinderRequestState
        {
            public GameFinderRequestState(Group group)
            {
                Group = group;
            }

            public TaskCompletionSource<IGameResolverContext> Tcs { get; } = new TaskCompletionSource<IGameResolverContext>();

            public RequestState State { get; set; } = RequestState.NotStarted;

            public Group Group { get; }

            public Game Candidate { get; set; }
        }

        private enum RequestState
        {
            NotStarted,
            Ready,
            Searching,
            Found,
            Validated,
            Rejected
        }
    }
}
