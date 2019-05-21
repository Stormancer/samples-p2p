using Stormancer;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server.GameFinder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2p
{
    class SampleGameFinderDataExtractor : IGameFinderDataExtractor
    {
        private readonly ILogger logger;

        public SampleGameFinderDataExtractor(ILogger logger)
        {
            this.logger = logger;
        }
        public Task<bool> ExtractData(string provider, RequestContext<IScenePeerClient> request, Group group)
        {
            //logger.Log(LogLevel.Info, "gamefinder", "Received player", new { group = group });
            var parameters = request.ReadObject<GameFinderParameters>();

            group.GroupData.Options = parameters;
            
            return Task.FromResult(true);
        }

        public Task<bool> ExtractDataS2S(string provider, Stream requestStream, Group group)
        {
            throw new NotImplementedException();
        }

        public void RefreshConfig(dynamic config)
        {
            
        }
    }
}
