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
using Newtonsoft.Json.Linq;
using Stormancer.Diagnostics;

namespace Stormancer.Server.GameSession
{
    class CompositeServerPoolProvider : IServerPoolProvider
    {

        private readonly Func<IServerPools> pools;
        private readonly ILogger logger;

        public CompositeServerPoolProvider(Func<IServerPools> pools, ILogger logger)
        {

            this.pools = pools;
            this.logger = logger;
        }
        public bool TryCreate(string id, JObject config, out IServerPool pool)
        {

            pool = new CompositeServerPool(id, pools(), logger);
            return true;

        }
    }

    class CompositeServerPool : IServerPool
    {
        private List<IServerPool> _subPools = new List<IServerPool>();
        private JObject config;
        private readonly IServerPools pools;
        private readonly ILogger logger;

        public int ServersReady => _subPools.Sum(p => p.ServersReady);

        public int ServersStarting => _subPools.Sum(p => p.ServersStarting);

        public int TotalServersInPool => ServersReady + ServersStarting;

        public int MinServerReady { get; private set; }

        public string Id { get; }

        public int ServersRunning => _subPools.Sum(p => p.ServersRunning);

        public int MaxServersInPool => _subPools.Sum(p => p.MaxServersInPool);

        public int PendingServerRequests => _subPools.Sum(p=>p.MaxServersInPool);

        public bool CanAcceptRequest => _subPools.Any(p => p.CanAcceptRequest);

        public CompositeServerPool(string id, IServerPools pools, ILogger logger)
        {
            Id = id;
            this.pools = pools;
            this.logger = logger;
        }
        public void Dispose()
        {

        }

        public async Task<Server> GetServer(string gameSessionId, GameSessionConfiguration config)
        {
            var candidate = _subPools.Reverse<IServerPool>().FirstOrDefault(p => p.ServersReady > 0);
            if (candidate != null)
            {
                return await candidate.GetServer(gameSessionId, config);
            }

            candidate = _subPools.FirstOrDefault(p => p.CanAcceptRequest);
            return await candidate.GetServer(gameSessionId, config);

        }

        public Task<GameServerStartupParameters> SetReady(string id, IScenePeerClient client)
        {
            throw new NotImplementedException();
        }
        public Task SetShutdown(string id)
        {
            throw new NotImplementedException();
        }
        public void UpdateConfiguration(JObject config)
        {
            this.config = config;
            dynamic d = config;
            MinServerReady = ((int?)d.ready) ?? 0;
            var poolIds = ((JArray)(d.pools))?.ToObject<List<string>>();
            _subPools.Clear();
            foreach (var poolId in poolIds)
            {
                var pool = pools.GetPool(poolId);
                _subPools.Add(pool);
            }
        }


    }
}
