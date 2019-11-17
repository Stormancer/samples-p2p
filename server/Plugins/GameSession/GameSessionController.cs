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
using Server.Plugins.API;
using Stormancer;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using System.Threading.Tasks;
using Stormancer.Server.GameSession.Models;
using Stormancer.Platform.Core.Cryptography;
using Stormancer.Server.Components;
using Stormancer.Server.Users;
using System;
using System.Linq;

namespace Stormancer.Server.GameSession
{
    class GameSessionController : ControllerBase
    {
        private readonly IGameSessionService _service;
        private readonly ILogger _logger;
        private readonly IUserSessions _sessions;
        private readonly IEnvironment _environment;

        public GameSessionController(IGameSessionService service, ILogger logger, IUserSessions sessions, IEnvironment environment)
        {
            _service = service;
            _logger = logger;
            _sessions = sessions;
            _environment = environment;
        }

        public async Task PostResults(RequestContext<IScenePeerClient> ctx)
        {
            var writer = await _service.PostResults(ctx.InputStream, ctx.RemotePeer);
            await ctx.SendValue(s =>
            {
                var oldPosition = s.Position;
                writer(s, ctx.RemotePeer.Serializer());
            });

        }

        [Api(ApiAccess.Public, ApiType.Rpc)]
        public async Task<string> GetP2PToken()
        {
            return await _service.CreateP2PToken(this.Request.RemotePeer.SessionId);
        }

        public Task Reset(RequestContext<IScenePeerClient> ctx)
        {
            return _service.Reset();
        }

        public async Task UpdateShutdownMode(RequestContext<IScenePeerClient> ctx)
        {
            ShutdownModeParameters shutdown = ctx.ReadObject<ShutdownModeParameters>();
            if (_service.IsHost(ctx.RemotePeer.SessionId))
            {
                await _service.UpdateShutdownMode(shutdown);
            }
            else
            {
                throw new ClientException("forbidden");
            }
        }

        [Api(ApiAccess.Public, ApiType.Rpc)]
        public async Task<string> GetUserFromBearerToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                _logger.Log(LogLevel.Error, "gamesession", $"Missing bearer token when calling GetUserFromBearerToken()", new { });
                throw new ClientException("Missing required token parameter");
            }

            var data = await _service.DecodeBearerToken(token);
            if (string.IsNullOrEmpty(data?.userId))
            {
                _logger.Log(LogLevel.Error, "gamesession", $"Failed to retrieve userId from bearer token data", new { data });
            }
            return data?.userId;
        }

        public async Task GetGameSessionSettings(RequestContext<IScenePeerClient> ctx)
        {
            var user = await _sessions.GetUser(ctx.RemotePeer);

            var config = _service.GetGameSessionConfig();
            if (string.IsNullOrEmpty(config.UserIds.FirstOrDefault(id => id == user?.Id)))
            {

                throw new ClientException($"unauthorized");
            }
        }

    }
}
