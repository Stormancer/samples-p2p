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
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Server.Plugins.Configuration;
using Stormancer.Diagnostics;
namespace Stormancer.Server.GameSession
{
    public interface IServerPools
    {
        IServerPool GetPool(string id);
    }
    public class ServerPools : IServerPools, IDisposable
    {
        private readonly ILogger logger;
        private readonly IConfiguration config;
        private readonly IEnumerable<IServerPoolProvider> providers;
        private readonly ConcurrentDictionary<string, IServerPool> _pools = new ConcurrentDictionary<string, IServerPool>();

        public ServerPools(ILogger logger, IConfiguration config, IEnumerable<IServerPoolProvider> providers)
        {
            this.logger = logger;
            this.config = config;
            this.providers = providers;
            config.SettingsChanged += OnSettingsChanged;
        }

        public void Dispose()
        {
            this.config.SettingsChanged -= OnSettingsChanged;
        }

        private void OnSettingsChanged(object sender, dynamic config)
        {
            var configs = (Dictionary<string, JObject>)(config.serverPools);
            var destroyedPools = new List<string>();
            foreach (var pool in _pools)
            {
                if (configs.TryGetValue(pool.Key, out var c))
                {
                    pool.Value.UpdateConfiguration(c);
                }
                else
                {
                    pool.Value?.Dispose();
                    destroyedPools.Add(pool.Key);

                }
            }
            foreach (var id in destroyedPools)
            {
                _pools.TryRemove(id, out _);
            }
        }
        
        private ConcurrentDictionary<string,string > _sessionToOnlineId = new ConcurrentDictionary<string, string>();
        internal Task<GameServerStartupParameters> SetReady(string onlineId, IScenePeerClient peer)
        {
            var poolId = onlineId.Split('/')[0];
            var id = onlineId.Split('/')[1];
            var pool = GetPool(poolId);
            if(pool == null)
            {
                throw new ArgumentException($"pool {poolId} not found");
            }
            _sessionToOnlineId.TryAdd(peer.SessionId, onlineId);
            return pool.SetReady(id, peer);
        }

        internal void SetShutdown(string sessionId)
        {
            if (_sessionToOnlineId.TryRemove(sessionId, out var onlineId))
            {
                var poolId = onlineId.Split('/')[0];
                var id = onlineId.Split('/')[1];
                var pool = GetPool(poolId);
                if (pool == null)
                {
                    throw new ArgumentException($"pool {poolId} not found");
                }
                pool.SetShutdown(id);
            }
        }

        private JObject GetConfiguration(string id)
        {
            if (((Dictionary<string, JObject>)config.Settings.serverPools).TryGetValue(id, out var c))
            {
                return c;
            }
            else
            {
                return null;
            }
        }
        public IServerPool GetPool(string id)
        {
            

            return _pools.GetOrAdd(id, _ =>
             {
                 var config = GetConfiguration(id);
                 if (config == null)
                 {
                     throw new ArgumentException($"No config found for pool {id}");
                 }

                 foreach (var provider in providers)
                 {
                     if (provider.TryCreate(id, config, out var pool))
                     {
                         return pool;
                     }

                 }
                 throw new InvalidOperationException($"Failed to create pool {id}. No provider could create the pool.");
             });
        }
    }
}
