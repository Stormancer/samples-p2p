using Stormancer.Server.GameFinder;
using Stormancer.Server.GameSession;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2p
{
    class SampleGameFindingResolver : IGameFinderResolver
    {
        private readonly IGameSessions gameSessions;

        public SampleGameFindingResolver(IGameSessions gameSessions)
        {
            this.gameSessions = gameSessions;
        }
        public Task PrepareGameResolution(GameFinderResult gameFinderResult)
        {
            return Task.CompletedTask;
        }

        public void RefreshConfig(dynamic config)
        {
            return;
        }

        public async Task ResolveGame(IGameResolverContext gameCtx)
        {
            var id = $"gs-{gameCtx.Game.Id}";
            try
            {
                await gameSessions.Create(P2pPlugin.GAMESESSION_TEMPLATE,id, new GameSessionConfiguration { Public = true });
            }
            catch(Exception)//The method throws an exception if the scene already exist.
            {

            }
            gameCtx.ResolutionAction = async ctx => {

                var response = new GameFinderResponse();

                response.connectionToken = await gameSessions.CreateConnectionToken(id, ctx.Peer.SessionId);
                
                ctx.WriteObjectToStream(response);
            };
        }
    }
}
