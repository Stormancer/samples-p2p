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
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stormancer.Diagnostics;
using Stormancer.Platform.Core.Cryptography;
using Stormancer.Server.Components;

namespace Stormancer.Server.GameSession
{
    class LocalServer : IGameServerProvider
    {
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly IDelegatedTransports pools;


        private class GameServerContainer : IDisposable
        {
            public Process ServerProcess { get; set; }
            public IDisposable ServerPortLease { get; set; }
            public IDisposable P2pPortLease { get; set; }
            public ushort ServerDedicatedPort { get; set; }
            public ushort ServerPort { get; set; }
            public string PublicIp { get; set; }

            public void Dispose()
            {
                ServerPortLease?.Dispose();
                P2pPortLease?.Dispose();
            }
        }

        private ConcurrentDictionary<string, GameServerContainer> _servers = new ConcurrentDictionary<string, GameServerContainer>();

        public string Type => "local";

        public LocalServer(
            IEnvironment env,
            IDelegatedTransports pools,
            ILogger logger)
        {
            _logger = logger;
            _environment = env;
            this.pools = pools;


        }

        public async Task<GameServer> StartServer(string id, JObject c, CancellationToken ct)
        {
            dynamic config = c;

            var applicationInfo = await _environment.GetApplicationInfos();

            var path = (string)config.executable;
            var verbose = ((bool?)config.verbose) ?? false;
            var log = ((bool?)config.log) ?? false;
            var stormancerPort = ((ushort?)config.stormancerPort) ?? 30000;
            var arguments = string.Join(" ", ((JArray)config.arguments ?? new JArray()).ToObject<IEnumerable<string>>());


            var server = await LeaseServerPort(c);



            var serverGuid = Guid.NewGuid();
            var result = new GameServer { Id = serverGuid };


            //Token used to authenticate the DS with the DedicatedServerAuthProvider
            var authenticationToken = TokenGenerator.CreateToken(id, applicationInfo.PrimaryKey);


            var startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.Arguments = $"PORT={server.ServerDedicatedPort.ToString()} { (log ? "-log" : "")} " + arguments; // { (log ? "-log" : "")}";//$"-port={_port} {(log ? "-log" : "")}";               
            startInfo.FileName = path ?? throw new InvalidOperationException("Missing 'pool.executable' configuration value");
            startInfo.CreateNoWindow = false;
            startInfo.UseShellExecute = false;

            //startInfo.EnvironmentVariables.Add("connectionToken", token);
            startInfo.EnvironmentVariables.Add("serverDedicatedPort", server.ServerDedicatedPort.ToString());
            startInfo.EnvironmentVariables.Add("clientSDKPort", server.ServerPort.ToString());
            startInfo.EnvironmentVariables.Add("serverPublicIp", server.PublicIp);
            startInfo.EnvironmentVariables.Add("localGridPort", stormancerPort.ToString());
            startInfo.EnvironmentVariables.Add("endPoint", applicationInfo.ApiEndpoint);
            startInfo.EnvironmentVariables.Add("accountID", applicationInfo.AccountId);
            startInfo.EnvironmentVariables.Add("applicationName", applicationInfo.ApplicationName);
            startInfo.EnvironmentVariables.Add("applicationtName", applicationInfo.ApplicationName); // DEPRECATED: Remove once dedicated server code is deployed!
            //startInfo.EnvironmentVariables.Add("serverMapStart", mapName);
            startInfo.EnvironmentVariables.Add("authentication.token", authenticationToken);

            var gmConfDto = new GameSessionConfigurationDto { Teams = config.Teams, Parameters = config.Parameters };
            var gameSessionsConfiguration = JsonConvert.SerializeObject(gmConfDto) ?? string.Empty;
            var b64gameSessionsConfiguration = Convert.ToBase64String(Encoding.UTF8.GetBytes(gameSessionsConfiguration));
            startInfo.EnvironmentVariables.Add("gameSessionConfiguration", b64gameSessionsConfiguration);
            _logger.Log(LogLevel.Debug, "gamesession", $"Starting server {startInfo.FileName} with args {startInfo.Arguments}", new { env = startInfo.EnvironmentVariables });


            server.ServerProcess = Process.Start(startInfo);


            server.ServerProcess.Exited += (sender, args) =>
            {
                if(_servers.TryRemove(id, out var container))
                {
                    container?.Dispose();
                }
                result.OnClosed?.Invoke();
            };

            _servers.TryAdd(id, server);


            return result;

        }

        public Task StopServer(string id)
        {
            if (_servers.TryRemove(id, out var server))
            {
                var prc = server.ServerProcess;
                if (prc != null && !prc.HasExited)
                {
                    try
                    {
                        _logger.Log(LogLevel.Info, "gameserver", $"Closing down game server for scene {id}.", new { prcId = prc.Id });
                        

                        if (!(prc?.HasExited ?? true))
                        {
                            _logger.Log(LogLevel.Error, "gameserver", $"Failed to close dedicated server. Killing it instead. The server should shutdown when receiving a message on the 'gameSession.shutdown' route.", new { prcId = prc.Id });
                            prc.Kill();
                        }

                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Error, "gameServer", "An error occured while closing the server.", ex);
                    }
                    finally
                    {
                        server.Dispose();
                    }

                }
                _logger.Log(LogLevel.Trace, "gameserver", $"Game server for scene {id} shut down.", new { id, P2PPort = server.ServerPort, ServerPort = server.ServerDedicatedPort });
            }

            return Task.CompletedTask;
        }


        private async Task<GameServerContainer> LeaseServerPort(dynamic config)
        {


            var p2pLease = await pools.AcquirePort((string)config.publicPool ?? "public1");
            if (!p2pLease.Success)
            {

                throw new InvalidOperationException("Unable to acquire port for the server");
            }
            var serverLease = await pools.AcquirePort((string)config.serverPool ?? "private1");
            if (!serverLease.Success)
            {

                throw new InvalidOperationException("Unable to acquire port for the server");
            }
            var server = new GameServerContainer
            {
                ServerPortLease = serverLease,
                P2pPortLease = p2pLease,
                ServerPort = serverLease.Port,
                ServerDedicatedPort = p2pLease.Port,
                PublicIp = p2pLease.PublicIp

            };
            return server;

        }
    }
}
