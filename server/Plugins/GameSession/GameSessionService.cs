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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Server.Plugins.Configuration;
using Stormancer.Core;
using Stormancer.Core.Helpers;
using Stormancer.Diagnostics;
using Stormancer.Platform.Core.Cryptography;
using Stormancer.Server.Components;
using Stormancer.Server.GameSession.Models;
using Stormancer.Server.Management;
using Stormancer.Server.Users;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.GameSession
{
    public enum ServerStatus
    {
        WaitingPlayers = 0,
        AllPlayersConnected = 1,
        Starting = 2,
        Started = 3,
        Shutdown = 4,
        Faulted = 5
    }

    public enum PlayerStatus
    {
        NotConnected = 0,
        Connected = 1,
        Ready = 2,
        Faulted = 3,
        Disconnected = 4
    }

    internal class GameSessionService : IGameSessionService, IDisposable
    {
        private class Client
        {
            public Client(IScenePeerClient peer)
            {
                Peer = peer;
                Reset();
            }

            public void Reset()
            {
                GameCompleteTcs?.TrySetCanceled();
                GameCompleteTcs = new TaskCompletionSource<Action<Stream, ISerializer>>();
                ResultData = null;
            }
            public IScenePeerClient Peer { get; set; }

            public Stream ResultData { get; set; }

            public PlayerStatus Status { get; set; }

            public string FaultReason { get; set; }

            public TaskCompletionSource<Action<Stream, ISerializer>> GameCompleteTcs { get; private set; }
        }

        // Constant variable
        private const string LOG_CATEOGRY = "Game session service";
        private const string P2P_TOKEN_ROUTE = "player.p2ptoken";
        private const string ALL_PLAYER_READY_ROUTE = "players.allReady";

        // Stormancer object

        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;
        private readonly ISceneHost _scene;
        private readonly IEnvironment _environment;
        private readonly IDelegatedTransports _pools;
        private readonly Func<IEnumerable<IGameSessionEventHandler>> _eventHandlers;
        private readonly IUserSessions _sessions;
        private TimeSpan _gameSessionTimeout = TimeSpan.MaxValue;
        private GameSessionConfiguration _config;
        private CancellationTokenSource _sceneCts = new CancellationTokenSource();
        // Dedicated game session 
        private System.Diagnostics.Process _gameServerProcess;
        private byte[] _serverGuid;

        private ConcurrentDictionary<string, Client> _clients = new ConcurrentDictionary<string, Client>();
        private ConcurrentDictionary<string, string> _sessionIdToUserIdMap = new ConcurrentDictionary<string, string>();
        private ServerStatus _status = ServerStatus.WaitingPlayers;

        private string _ip = "";
        private IDisposable _serverPortLease;
        private ushort _serverPort;
        private IDisposable _p2pPortLease;
        private ushort _serverDedicatedPort;
        private string _p2pToken;
        private bool _serverEnabled;

        private readonly object _lock = new object();
        private readonly ManagementClientAccessor _management;
        private TaskCompletionSource<IScenePeerClient> _serverPeer = null;
        private ShutdownMode _shutdownMode;
        private DateTime _shutdownDate;



        public GameSessionService(
            ISceneHost scene,
            IConfiguration configuration,
            IEnvironment environment,
            IDelegatedTransports pools,
            ManagementClientAccessor management,
            ILogger logger,
            Func<IEnumerable<IGameSessionEventHandler>> eventHandlers,
            IUserSessions sessions)
        {
            _management = management;
            _scene = scene;
            _configuration = configuration;
            _logger = logger;
            _environment = environment;
            _pools = pools;

            _eventHandlers = eventHandlers;
            _sessions = sessions;

            _configuration.SettingsChanged += OnSettingsChange;
            OnSettingsChange(_configuration, _configuration.Settings);

            scene.Shuttingdown.Add(args =>
            {
                _sceneCts.Cancel();
                return Task.CompletedTask;
            });
            scene.Connecting.Add(this.PeerConnecting);
            scene.Connected.Add(this.PeerConnected);
            scene.Disconnected.Add((args) => this.PeerDisconnecting(args.Peer));
            scene.AddRoute("player.ready", ReceivedReady, _ => _);
            scene.AddRoute("player.faulted", ReceivedFaulted, _ => _);
        }

        private void OnSettingsChange(Object sender, dynamic settings)
        {
            _serverEnabled = ((bool?)settings?.gameServer?.dedicatedServer) ?? false;
            var timeout = ((string)settings?.gameServer?.dedicatedServerTimeout);
            if (timeout != null)
            {
                _gameSessionTimeout = TimeSpan.Parse(timeout, CultureInfo.InvariantCulture);
            }
            else
            {
                _gameSessionTimeout = TimeSpan.MaxValue;
            }
        }

        private async Task<string> GetUserId(IScenePeerClient peer)
        {
            if (_config.userIds.TryGetValue(peer.SessionId, out var userId))
            {
                return userId;
            }
            else
            {
                return (await _sessions.GetUser(peer)).Id;
            }

        }

        private string RemoveUserId(IScenePeerClient peer)
        {
            _sessionIdToUserIdMap.TryRemove(peer.SessionId, out string userId);
            return userId;
        }

        private async Task ReceivedReady(Packet<IScenePeerClient> packet)
        {
            try
            {
                var peer = packet.Connection;
                if (peer == null)
                {
                    throw new ArgumentNullException("peer");
                }
                if (peer.ContentType == "application/server-id")
                {
                    var peerGuid = new Guid(peer.UserData);
                    var serverGuid = new Guid(_serverGuid);
                    if (serverGuid == peerGuid)
                    {
                        await SignalServerReady(peer.SessionId);
                        return;
                    }

                }
                if (IsWorker(packet.Connection))
                {
                    if (_status == ServerStatus.Started)
                    {
                        await _scene.Send(new MatchPeerFilter(peer), P2P_TOKEN_ROUTE, s =>
                        {
                            var serializer = peer.Serializer();
                            serializer.Serialize(_p2pToken, s);
                        }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                    }
                    return;
                }

                var user = await GetUserId(peer);

                if (user == null)
                {
                    throw new InvalidOperationException("Unauthenticated peer.");
                }

                if (!_clients.TryGetValue(user, out Client currentClient))
                {
                    throw new InvalidOperationException("Unknown client.");
                }

                _logger.Log(LogLevel.Trace, "gamesession", "received a ready message from an user.", new { userId = user, currentClient.Status });

                if (currentClient.Status < PlayerStatus.Ready)
                {
                    currentClient.Status = PlayerStatus.Ready;

                    BroadcastClientUpdate(currentClient, user, packet.ReadObject<string>());
                }

                await CheckAllPlayersReady();

                if (IsHost(peer.SessionId) && (((bool?)_configuration.Settings.gameSession?.usep2p) == true))
                {
                    var p2pToken = await _scene.DependencyResolver.Resolve<IPeerInfosService>().CreateP2pToken(peer.SessionId, _scene.Id);

                    _p2pToken = p2pToken;

                    foreach (var p in _scene.RemotePeers.Where(p => p != peer))
                    {
                        p.Send(P2P_TOKEN_ROUTE, p2pToken);
                    }

                }

            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "gamesession", "an error occurred while receiving a ready message", ex);
                throw;
            }
        }

        // pseudoBool to use with interlocked
        private int _readySent = 0;
        private async Task CheckAllPlayersReady()
        {
            if (_config.userIds.Count == _clients.Count)
            {
                if (_clients.Values.All(c => c.Status == PlayerStatus.Ready) && System.Threading.Interlocked.CompareExchange(ref _readySent, 1, 0) == 0)
                {
                    _logger.Log(LogLevel.Trace, "gamesession", "Send all player ready", new { });
                    await _scene.Send(new MatchAllFilter(), ALL_PLAYER_READY_ROUTE, s => { }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                }
            }
        }

        private void BroadcastClientUpdate(Client client, string userId, string data = null)
        {
            _scene.Broadcast("player.update", new PlayerUpdate { UserId = userId, Status = (byte)client.Status, Data = data ?? "", IsHost = (_config.HostUserId == userId) }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_ORDERED);
        }

        private async Task ReceivedFaulted(Packet<IScenePeerClient> packet)
        {
            var peer = packet.Connection;
            if (peer == null)
            {
                throw new ArgumentNullException("peer");
            }
            var user = await GetUserId(peer);

            if (user == null)
            {
                throw new InvalidOperationException("Unauthenticated peer.");
            }

            if (!_clients.TryGetValue(user, out Client currentClient))
            {
                throw new InvalidOperationException("Unknown client.");
            }

            var reason = packet.ReadObject<string>();
            currentClient.Status = PlayerStatus.Faulted;

            if (this._status == ServerStatus.WaitingPlayers
                || this._status == ServerStatus.AllPlayersConnected)
            {
                this._status = ServerStatus.Faulted;


            }


            //return Task.CompletedTask;
        }

        public void SetConfiguration(dynamic metadata)
        {
            if (metadata.gameSession != null)
            {
                _config = ((JObject)metadata.gameSession).ToObject<GameSessionConfiguration>();
            }
        }

        private bool IsWorker(IScenePeerClient peer)
        {
            return peer.ContentType == "application/server-id";
        }

        private async Task PeerConnecting(IScenePeerClient peer)
        {
            if (peer.ContentType == "application/server-id")
            {
                if (IsServer(peer))
                {
                    //return Task.CompletedTask; ;
                }
                else
                {
                    throw new ClientException("Failed to authenticate as dedicated server");
                }
            }

            if (IsWorker(peer))
            {
                //return Task.CompletedTask; ;
            }
            if (peer == null)
            {
                throw new ArgumentNullException("peer");
            }
            var user = await GetUserId(peer);

            if (user == null)
            {
                throw new ClientException("You are not authenticated.");
            }

            if (_config == null)
            {
                throw new InvalidOperationException("Game session plugin configuration missing in scene instance metadata. Please check the scene creation process.");
            }
            if (user == null && !_config.Public)
            {
                throw new ClientException("You are not authorized to join this game.");
            }

            var client = new Client(peer);

            if (user != null && !_clients.TryAdd(user, client))
            {
                if (_clients.TryGetValue(user, out Client alreadyConnectedClient) && alreadyConnectedClient.Status != PlayerStatus.Disconnected && !_clients.TryUpdate(user, client, alreadyConnectedClient))
                {
                    throw new ClientException("Failed to add player to the game session.");
                }

            }

            client.Status = PlayerStatus.Connected;
            if (!_config.Public)
            {
                BroadcastClientUpdate(client, user);
            }
            //return Task.CompletedTask;
        }

        private async Task SignalServerReady(string sessionId)
        {
            _p2pToken = await _scene.DependencyResolver.Resolve<IPeerInfosService>().CreateP2pToken(sessionId, _scene.Id);
            _logger.Log(LogLevel.Trace, "gameserver", "Server responded as ready.", new { Port = _serverPort, P2PPort = _serverDedicatedPort });

            foreach (var p in _scene.RemotePeers.Where(p => p.SessionId != sessionId))
            {
                p.Send(P2P_TOKEN_ROUTE, _p2pToken);
            }
            //_scene.Broadcast(P2P_TOKEN_ROUTE, _p2pToken);
            _status = ServerStatus.Started;
        }

        public bool IsServer(IScenePeerClient peer)
        {
            if (peer != null && peer.ContentType == "application/server-id")
            {
                var peerGuid = new Guid(peer.UserData);
                var serverGuid = new Guid(_serverGuid);
                return serverGuid == peerGuid;
            }
            else
            {
                return false;
            }
        }

        private async Task PeerConnected(IScenePeerClient peer)
        {
            await TryStart();

            if (IsServer(peer))
            {
                GetServerTcs().TrySetResult(peer);


                peer.Send(P2P_TOKEN_ROUTE, "");
                return;
            }
            if (!IsWorker(peer))
            {
                if (peer == null)
                {
                    throw new ArgumentNullException("peer");
                }
                var userId = await GetUserId(peer);

                if (userId == null)
                {
                    throw new ClientException("You are not authenticated.");
                }

                //Check if the gameSession is Dedicated or listen-server            
                if (!_serverEnabled)
                {
                    // If the host is not defined a P2P was sent with "" to notify client is host.
                    _logger.Log(LogLevel.Trace, "gamesession", $"Gamesession {_scene.Id} evaluating {userId} as host (expected host :{_config.HostUserId})", new { });
                    if (string.IsNullOrEmpty(_config.HostUserId) || _config.HostUserId == userId)
                    {
                        if (GetServerTcs().TrySetResult(peer))
                        {
                            _logger.Log(LogLevel.Debug, LOG_CATEOGRY, "Host defined and connecting", userId);
                            peer.Send(P2P_TOKEN_ROUTE, "");
                        }
                        else
                        {
                            _logger.Log(LogLevel.Debug, LOG_CATEOGRY, "Client connecting", userId);
                        }
                    }
                }
                else
                {

                }
               

                foreach (var uId in _clients.Keys)
                {
                    if (uId != userId)
                    {
                        var currentClient = _clients[uId];
                       
                        var isHost = GetServerTcs().Task.IsCompleted && GetServerTcs().Task.Result.SessionId == currentClient.Peer.SessionId;
                        peer.Send("player.update",
                            new PlayerUpdate { UserId = uId, IsHost = isHost,  Status = (byte)currentClient.Status, Data = currentClient.FaultReason ?? "" },
                            PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_ORDERED);
                    }
                }
            }
            if (_status == ServerStatus.Started)
            {
                if (_p2pToken == null && GetServerTcs().Task.IsCompleted)
                {
                    _p2pToken = await _scene.DependencyResolver.Resolve<IPeerInfosService>().CreateP2pToken((await GetServerTcs().Task).SessionId, _scene.Id);
                }

                peer.Send(P2P_TOKEN_ROUTE, _p2pToken);
            }
        }

        private Task _serverStartTask = null;
        public Task TryStart()
        {
            lock (this._lock)
            {
                if (_serverStartTask == null)
                {
                    _serverStartTask = Start();
                }

            }
            return _serverStartTask;
        }

        private async Task Start()
        {
            var ctx = new GameSessionContext(this._scene, this._config, this);
            await _eventHandlers().RunEventHandler(h => h.GameSessionStarting(ctx), ex => _logger.Log(LogLevel.Error, "gameSession", "An error occured while executing GameSessionStarting event", ex));
            var applicationInfo = await _environment.GetApplicationInfos();

            var path = (string)_configuration.Settings?.gameServer?.executable;
            var verbose = ((bool?)_configuration.Settings?.gameServer?.verbose) ?? false;
            var log = ((bool?)_configuration.Settings?.gameServer?.log) ?? false;
            var stormancerPort = ((ushort?)_configuration.Settings?.gameServer?.stormancerPort) ?? 30000;
            var arguments = string.Join(" ", ((JArray)_configuration.Settings?.gameServer?.arguments ?? new JArray()).ToObject<IEnumerable<string>>());
            var mapName = _config.Parameters?.GetValue("mapName")?.Value<string>() ?? null;


            if (!_serverEnabled)
            {
                _logger.Log(LogLevel.Trace, "gamesession", "No server executable enabled. Game session started.", new { });
                _status = ServerStatus.Started;
                return;
            }

            try
            {

                if (path == null)
                {
                    throw new InvalidOperationException("Missing 'gameServer.executable' configuration value");
                }

                if (mapName == null)
                {
                    throw new InvalidOperationException("Missing gameFinder.[gameFinderKind].mapName configuration value");
                }



                await LeaseServerPort();
                var managementClient = await _management.GetApplicationClient();
                _serverGuid = Guid.NewGuid().ToByteArray();
                var token = await managementClient.CreateConnectionToken(_scene.Id, _serverGuid, "application/server-id");

                //Token used to authenticate the DS with the DedicatedServerAuthProvider
                var authenticationToken = TokenGenerator.CreateToken(this._scene.Id, applicationInfo.PrimaryKey);


                var startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.Arguments = $"PORT={_serverDedicatedPort.ToString()} { (log ? "-log" : "")} " + arguments; // { (log ? "-log" : "")}";//$"-port={_port} {(log ? "-log" : "")}";               
                startInfo.FileName = path;
                startInfo.CreateNoWindow = false;
                startInfo.UseShellExecute = false;

                startInfo.EnvironmentVariables.Add("connectionToken", token);
                startInfo.EnvironmentVariables.Add("serverDedicatedPort", _serverDedicatedPort.ToString());
                startInfo.EnvironmentVariables.Add("clientSDKPort", _serverPort.ToString());
                startInfo.EnvironmentVariables.Add("serverPublicIp", _ip);
                startInfo.EnvironmentVariables.Add("localGridPort", stormancerPort.ToString());
                startInfo.EnvironmentVariables.Add("endPoint", applicationInfo.ApiEndpoint);
                startInfo.EnvironmentVariables.Add("accountID", applicationInfo.AccountId);
                startInfo.EnvironmentVariables.Add("applicationtName", applicationInfo.ApplicationName);
                startInfo.EnvironmentVariables.Add("serverMapStart", mapName);
                startInfo.EnvironmentVariables.Add("authentication.token", authenticationToken);

                var gmConfDto = new GameSessionConfigurationDto { Teams = _config.Teams, Parameters = _config.Parameters };
                var gameSessionsConfiguration = JsonConvert.SerializeObject(gmConfDto) ?? string.Empty;
                var b64gameSessionsConfiguration = Convert.ToBase64String(Encoding.UTF8.GetBytes(gameSessionsConfiguration));
                startInfo.EnvironmentVariables.Add("gameSessionConfiguration", b64gameSessionsConfiguration);
                _logger.Log(LogLevel.Debug, "gamesession", $"Starting server {startInfo.FileName} with args {startInfo.Arguments}", new { env = startInfo.EnvironmentVariables });

                //prc.OutputDataReceived += (sender, args) =>
                //{
                //    if (verbose)
                //    {
                //        _logger.Log(LogLevel.Trace, "gameserver", "Received data output from Intrepid server.", new { args.Data });
                //    }


                //};
                //prc.ErrorDataReceived += (sender, args) =>
                //  {
                //      _logger.Error("gameserver", $"An error occured while trying to start the game server : '{args.Data}'");
                //  };

                var prc = Process.Start(startInfo);
                CancellationToken serverCt = _sceneCts.Token;
                if (_gameSessionTimeout != TimeSpan.MaxValue)
                {
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
                    cts.CancelAfter(_gameSessionTimeout);
                    serverCt = cts.Token;

                }
                serverCt.Register(() => { _ = CloseGameServerProcess(); });
                if (prc != null)
                    _logger.Log(LogLevel.Debug, "gamesession", "Starting process success ", new { });
                else
                {
                    _serverStartTask = null;
                    _logger.Log(LogLevel.Debug, "gamesession", "Starting process failed ", new { });
                }

                prc.Exited += (sender, args) =>
                {
                    try
                    {
                        _p2pToken = null;
                        _logger.Error("gamesession", "Server stopped");
                        _status = ServerStatus.Shutdown;
                        foreach (var client in _clients.Values)
                        {
                            // FIXME: Temporary workaround to issue where disconnections cause large increases in CPU/Memory usage
                            //client.Peer?.Disconnect("Game server stopped");
                        }
                        if (_config.canRestart)
                        {
                            _status = ServerStatus.WaitingPlayers;
                            _serverStartTask = null;
                            Reset();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Error, "gamesession", "An unhandled exception occured on dedicated server exit", ex);
                    }
                };

                _gameServerProcess = prc;






            }
            catch (Exception ex)
            {
                _serverStartTask = null;
                _logger.Log(LogLevel.Error, "gameserver", "Failed to start server.", ex);
                if (_config.canRestart)
                {
                    _status = ServerStatus.WaitingPlayers;
                    await Reset();
                }
                else
                {
                    _status = ServerStatus.Shutdown;
                }
                foreach (var client in _clients.Values)
                {
                    // FIXME: Temporary workaround to issue where disconnections cause large increases in CPU/Memory usage
                    //await client.Peer.Disconnect("Game server stopped");
                }
            }
        }

        private async Task LeaseServerPort()
        {


            var p2pLease = await _pools.AcquirePort((string)_configuration.Settings?.gameServer?.publicPool ?? "public1");
            if (!p2pLease.Success)
            {

                throw new InvalidOperationException("Unable to acquire port for the server");
            }
            var serverLease = await _pools.AcquirePort((string)_configuration.Settings?.gameServer?.serverPool ?? "private1");
            if (!serverLease.Success)
            {

                throw new InvalidOperationException("Unable to acquire port for the server");
            }
            _serverPortLease = serverLease;
            _p2pPortLease = p2pLease;
            _serverDedicatedPort = p2pLease.Port;
            _serverPort = serverLease.Port;
            _ip = p2pLease.PublicIp;

        }

        private TaskCompletionSource<IScenePeerClient> GetServerTcs()
        {
            lock (_lock)
            {
                if (_serverPeer == null)
                {
                    _serverPeer = new TaskCompletionSource<IScenePeerClient>();
                }
            }
            return _serverPeer;
        }
        public async Task PeerDisconnecting(IScenePeerClient peer)
        {
            if (IsHost(peer.SessionId))
            {
                lock (_lock)
                {
                    _serverPeer = null;
                }
            }
            if (peer == null)
            {
                throw new ArgumentNullException("peer");
            }
            var user = RemoveUserId(peer);

            Client client = null;
            string userId = null;

            // the peer disconnected from the app and is not in the sessions anymore.
            foreach (var kvp in _clients)
            {
                if (kvp.Value.Peer == peer)
                {
                    userId = kvp.Key;

                    if (_config.Public)
                    {
                        _clients.TryRemove(userId, out client);
                    }
                    // no need to continue searching for the client, we already found it
                    break;
                }
            }


            if (client != null)
            {

                client.Peer = null;
                client.Status = PlayerStatus.Disconnected;

                BroadcastClientUpdate(client, userId);
                await EvaluateGameComplete();
            }

            if (_shutdownMode == ShutdownMode.NoPlayerLeft)
            {
                if (!_clients.Values.Any(c => c.Status != PlayerStatus.Disconnected))
                {
                    var _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000 * 60);
                        if (!_clients.Values.Any(c => c.Status != PlayerStatus.Disconnected))
                        {
                            await CloseGameServerProcess();
                        }
                    });
                }
            }
        }

        private async Task CloseGameServerProcess()
        {
            if (_gameServerProcess != null && !_gameServerProcess.HasExited)
            {
                try
                {
                    _logger.Log(LogLevel.Info, "gameserver", $"Closing down game server for scene {_scene.Id}.", new { prcId = _gameServerProcess.Id });
                    if (_serverPeer != null)
                    {
                        (await _serverPeer.Task).Send("gameSession.shutdown", s => { }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                        //_gameServerProcess.Close();
                        _serverPeer = null;
                        await Task.Delay(10000).ContinueWith(t =>
                        {
                            if (!(_gameServerProcess?.HasExited ?? true))
                            {
                                _logger.Log(LogLevel.Error, "gameserver", $"Failed to close dedicated server. Killing it instead. The server should shutdown when receiving a message on the 'gameSession.shutdown' route.", new { prcId = _gameServerProcess.Id });
                                _gameServerProcess.Kill();
                            }

                        });
                    }
                    else
                    {
                        if (!(_gameServerProcess?.HasExited ?? true))
                        {
                            _logger.Log(LogLevel.Error, "gameserver", $"The dedicated server didn't connect to the game session. Graceful shutdown impossible, killing it instead.", new { prcId = _gameServerProcess.Id });
                            _gameServerProcess.Kill();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, "gameServer", "An error occured while closing the server.", ex);
                }
                finally
                {
                    _gameServerProcess = null;
                    _p2pPortLease?.Dispose();
                    _serverPortLease?.Dispose();
                }

            }
            _logger.Log(LogLevel.Trace, "gameserver", $"Game server for scene {_scene.Id} shut down.", new { _scene.Id, P2PPort = _serverDedicatedPort, ServerPort = _serverPort });
        }

        public Task Reset()
        {
            foreach (var client in _clients.Values)
            {
                client.Reset();
            }
            return Task.FromResult(0);
        }

        public async Task<Action<Stream, ISerializer>> PostResults(Stream inputStream, IScenePeerClient remotePeer)
        {

            if (!_serverEnabled)
            {
                if (this._status != ServerStatus.Started)
                {
                    throw new ClientException($"Unable to post result before game session start. Server status is {this._status}");
                }
                var userId = await GetUserId(remotePeer);
                if (userId != null)
                {
                    _clients[userId].ResultData = inputStream;

                    await EvaluateGameComplete();
                    return await _clients[userId].GameCompleteTcs.Task;
                }
                else
                {
                    throw new ClientException("unauthorized?reason=publicGame");
                }
            }
            else
            {
                if (!IsHost(remotePeer.SessionId))
                {
                    throw new ClientException("Operation forbidden");
                }

                await EvaluateGameComplete(inputStream);
                return (Stream stream, ISerializer s) => { };
            }
        }

        private async Task EvaluateGameComplete(Stream inputStream)
        {
            await CloseGameServerProcess();
            var ctx = new GameSessionCompleteCtx(this, _scene, _config, new[] { new GameSessionResult("", null, inputStream) }, _clients.Keys);
            await _eventHandlers()?.RunEventHandler(eh => eh.GameSessionCompleted(ctx), ex =>
            {
                _logger.Log(LogLevel.Error, "gameSession", "An error occured while running gameSession.GameSessionCompleted event handlers", ex);
            });

            // FIXME: Temporary workaround to issue where disconnections cause large increases in CPU/Memory usage
            //await Task.WhenAll(_scene.RemotePeers.Select(user => user.Disconnect("Game complete")));


            await _scene.KeepAlive(TimeSpan.Zero);

        }
        private AsyncLock _asyncLock = new AsyncLock();
        private async Task EvaluateGameComplete()
        {
            using (await _asyncLock.LockAsync())
            {
                if (_clients.Values.All(c => c.ResultData != null || c.Peer == null))//All remaining clients sent their data
                {
                    var ctx = new GameSessionCompleteCtx(this, _scene, _config, _clients.Select(kvp => new GameSessionResult(kvp.Key, kvp.Value.Peer, kvp.Value.ResultData)), _clients.Keys);

                    await _eventHandlers()?.RunEventHandler(eh => eh.GameSessionCompleted(ctx), ex =>
                    {
                        _logger.Log(LogLevel.Error, "gameSession", "An error occured while running gameSession.GameSessionCompleted event handlers", ex);
                        foreach (var client in _clients.Values)
                        {
                            client.GameCompleteTcs.TrySetException(ex);
                        }
                    });

                    foreach (var client in _clients.Values)
                    {
                        client.GameCompleteTcs.TrySetResult(ctx.ResultsWriter);
                    }

                    // FIXME: Temporary workaround to issue where disconnections cause large increases in CPU/Memory usage
                    //await Task.WhenAll(_scene.RemotePeers.Select(user => user.Disconnect("Game complete")));

                    await _scene.KeepAlive(TimeSpan.Zero);
                }
            }
        }

        public async Task<string> CreateP2PToken(string sessionId)
        {
            var hostPeer = await GetServerTcs().Task;
            if (sessionId == hostPeer.SessionId)
            {
                return null;
            }
            else
            {
                return await _scene.DependencyResolver.Resolve<IPeerInfosService>().CreateP2pToken(hostPeer.SessionId, _scene.Id);
            }

        }

        public async Task UpdateShutdownMode(ShutdownModeParameters shutdown)
        {


            if (shutdown.shutdownMode == ShutdownMode.SceneShutdown)
            {
                _shutdownMode = shutdown.shutdownMode;
                _shutdownDate = await _scene.KeepAlive(new TimeSpan(0, 0, shutdown.keepSceneAliveFor));

            }

        }

        public bool IsHost(string sessionId)
        {

            if (!GetServerTcs().Task.IsCompleted)
            {
                return false;
            }
            return sessionId == GetServerTcs().Task.Result.SessionId;

        }

        public void Dispose()
        {
            CloseGameServerProcess().Wait();
        }

        public GameSessionConfigurationDto GetGameSessionConfig()
        {

            return new GameSessionConfigurationDto { Teams = _config.Teams, Parameters = _config.Parameters, UserIds = _config.userIds.Values };
        }

        public async Task<BearerTokenData> DecodeBearerToken(string token)
        {
            var app = await _environment.GetApplicationInfos();

            var bearerTokenDecodingKey = app.PrimaryKey;
            var userSessionsAppId = _configuration.Settings?.gameSession?.userSessionsAppId?.ToString();
            if (userSessionsAppId != null)
            {
                var accessKeys = _configuration.Settings?.management?.accessKeys;
                var key = accessKeys[userSessionsAppId]?.ToString();
                if (key != null)
                {
                    bearerTokenDecodingKey = key;
                }
            }

            var data = TokenGenerator.DecodeToken<BearerTokenData>(token, bearerTokenDecodingKey);
            if (data == null)
            {
                throw new ClientException("Invalid Token");
            }
            return data;
        }
    }
}
