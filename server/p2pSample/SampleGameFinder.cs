using System.Threading.Tasks;
using Stormancer.Server.GameFinder;

namespace P2p
{
    internal class SampleGameFinder : IGameFinder
    {
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

        public void RefreshConfig(dynamic specificConfig, dynamic config)
        {
            
        }
    }
}