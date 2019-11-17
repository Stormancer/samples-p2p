using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Stormancer.Server.GameFinder;

namespace P2p
{
    internal class SampleGameFinder : IGameFinder
    {
        public JObject ComputeDataAnalytics(GameFinderContext gameFinderContext)
        {
            throw new System.NotImplementedException();
        }

        public Task<GameFinderResult> FindGames(GameFinderContext gameFinderContext)
        {
            var results = new GameFinderResult();

            foreach(var group in gameFinderContext.WaitingClient)
            {
                var game = new Game { Id = group.Options<GameFinderParameters>().GameId};
                var team = new Team();
                team.Groups.Add(group);
                game.Teams.Add(team);
                results.Games.Add(game);
            }
            return Task.FromResult(results);
        }

        public Dictionary<string, int> GetMetrics()
        {
            return new Dictionary<string, int>();
        }

        public void RefreshConfig(dynamic specificConfig, dynamic config)
        {
            
        }
    }
}