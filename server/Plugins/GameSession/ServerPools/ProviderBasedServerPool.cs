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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Stormancer.Core;
using Stormancer.Diagnostics;

namespace Stormancer.Server.GameSession
{
    class ProviderBasedServerPoolProvider : IServerPoolProvider
    {
        private readonly IEnumerable<IGameServerProvider> serverProviders;
        private readonly ILogger logger;
        private readonly IGameSessions gameSessions;

        public ProviderBasedServerPoolProvider(IEnumerable<IGameServerProvider> serverProviders, ILogger logger, IGameSessions gameSessions)
        {
            this.serverProviders = serverProviders;
            this.logger = logger;
            this.gameSessions = gameSessions;
        }
        public bool TryCreate(string id, JObject config, out IServerPool pool)
        {
            pool = null;
            var d = (dynamic)config;
            var pId = (string)d?.provider;
            if (pId == null)
            {
                return false;
            }
            var provider = serverProviders.FirstOrDefault(p => p.Type == pId);

            if (provider == null)
            {
                return false;
            }
            pool = new ProviderBasedServerPool(id, provider, logger, gameSessions);
            return true;

        }
    }

    class ProviderBasedServerPool : IServerPool
    {
        private class GameServerRequest
        {
            public TaskCompletionSource<Server> RequestCompletedCompletionSource { get; set; }
            public GameSessionConfiguration GameSessionConfiguration { get; set; }
            public string Id { get; set; }
        }

        private readonly IGameServerProvider provider;
        private readonly ILogger logger;
        private JObject config;
        private ConcurrentDictionary<string, Server> _startingServers = new ConcurrentDictionary<string, Server>();
        private ConcurrentDictionary<string, Server> _readyServers = new ConcurrentDictionary<string, Server>();
        private ConcurrentDictionary<string, Server> _runningServers = new ConcurrentDictionary<string, Server>();
        private ConcurrentQueue<GameServerRequest> _pendingRequests = new ConcurrentQueue<GameServerRequest>();

        private bool isRunning = false;
        public string Id { get; }
        public ProviderBasedServerPool(string id, IGameServerProvider provider, ILogger logger, IGameSessions gameSessions)
        {
            Id = id;
            this.provider = provider;
            this.logger = logger;
            Task.Run(async () =>
            {
                isRunning = true;
                while (isRunning)
                {
                    try
                    {
                        // resolve pending requests
                        while (_pendingRequests.Any() && _readyServers.Any())
                        {
                            if (_pendingRequests.TryDequeue(out var request))
                            {
                                var serverId = _readyServers.Keys.FirstOrDefault();

                                if (serverId != null && _readyServers.TryRemove(serverId, out var server))
                                {


                                    request.RequestCompletedCompletionSource.SetResult(server);

                                    _runningServers.TryAdd(serverId, server);
                                    var startupParameters = new GameServerStartupParameters
                                    {
                                        GameSessionConnectionToken = await gameSessions.CreateServerConnectionToken(request.Id, server.GameServer.Id),
                                        Config = request.GameSessionConfiguration,
                                        GameSessionId = request.Id
                                    };
                                    server.RunTcs.TrySetResult(startupParameters);
                                }
                                else
                                {
                                    _pendingRequests.Enqueue(request);
                                }
                            }
                        }

                        //meet running servers requirements to satisfy requests + min ready server requirements
                        var serversToStart = _pendingRequests.Count - _readyServers.Count - _startingServers.Count + MinServerReady;

                        for (int i = 0; i < serversToStart; i++)
                        {
                            var guid = Guid.NewGuid().ToString();
                            _ = StartServer(provider.Type + "/" + guid);
                        }

                       

                        //clean timedout servers
                        foreach (var server in _startingServers.Values.ToArray())
                        {
                            if (server.CreatedOn < DateTime.UtcNow - TimeSpan.FromMinutes(10))
                            {
                                _startingServers.TryRemove(server.Id, out _);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log(LogLevel.Fatal, "serverpools." + provider.Type, "An error occured while running the server pool logic", ex);
                    }
                    await Task.Delay(1000);
                }

                foreach (var rq in _pendingRequests)
                {
                    rq.RequestCompletedCompletionSource.TrySetCanceled();
                }

                while (_startingServers.Any() || _readyServers.Any())
                {
                    foreach (var server in _readyServers.Values)
                    {
                        await provider.StopServer(server.Id);
                    }
                    await Task.Delay(1000);
                }
            });
        }


        private async Task StartServer(string id)
        {
            var server = new Server { Id = id, CreatedOn = DateTime.UtcNow };
            _startingServers.TryAdd(id, server);
            var cts = new CancellationTokenSource(10 * 60 * 1000);
            try
            {
                var gs = await provider.StartServer(id, config, cts.Token);
                server.GameServer = gs;
                gs.OnClosed += () =>
                {
                    _runningServers.TryRemove(id, out _);
                };
            }
            catch (Exception ex)
            {
                logger.Log(LogLevel.Fatal, "serverpools." + provider.Type, "An error occured while trying to create a server in the pool", ex);
                _startingServers.TryRemove(id, out _);
            }

        }

        public int ServersReady => _readyServers.Count;

        public int ServersStarting => _startingServers.Count;

        public int TotalServersInPool => ServersReady + ServersStarting + ServersRunning;

        public int MinServerReady { get; private set; }
        public int MaxServersInPool { get; private set; }

        public int ServersRunning => _runningServers.Count;

        public int PendingServerRequests => _pendingRequests.Count;

        public bool CanAcceptRequest => ServersRunning + PendingServerRequests < MaxServersInPool;

        public void Dispose()
        {
            isRunning = false;
        }

        public Task<Server> GetServer(string gameSessionId, GameSessionConfiguration config)
        {
            if (!isRunning)
            {
                throw new InvalidOperationException("Pool not running");
            }
            var tcs = new TaskCompletionSource<Server>();
            _pendingRequests.Enqueue(new GameServerRequest { RequestCompletedCompletionSource = tcs, Id = gameSessionId, GameSessionConfiguration = config });

            return tcs.Task;
        }

        public void UpdateConfiguration(JObject config)
        {
            this.config = config;
            dynamic d = config;
            MinServerReady = ((int?)d.ready) ?? 0;
            MaxServersInPool = ((int?)d.ready) ?? int.MaxValue;
        }

        public Task<GameServerStartupParameters> SetReady(string id, IScenePeerClient client)
        {
            if (_startingServers.TryRemove(id, out var server))
            {
                server.Peer = client;
                server.RunTcs = new TaskCompletionSource<GameServerStartupParameters>();
                _readyServers.TryAdd(id, server);
                return server.RunTcs.Task;
            }

            throw new ClientException($"dedicatedServer.notInPool");


        }

        public async Task SetShutdown(string gameId)
        {
            if (_runningServers.TryRemove(gameId, out var server))
            {
                if (server.Peer != null)
                {
                    server.Peer.Send("gameSession.shutdown", s => { }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                    await Task.Delay(10000);
                }

                await provider.StopServer(gameId);
            }

        }
    }
}
