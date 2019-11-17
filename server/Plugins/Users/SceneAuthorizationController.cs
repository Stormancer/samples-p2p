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
using Stormancer.Platform.Core.Cryptography;
using Stormancer.Plugins;
using Stormancer.Server.Components;
using System;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    internal class SceneAuthorizationController : ControllerBase
    {
        private readonly Management.ManagementClientAccessor _accessor;
       
        private readonly IUserSessions _sessions;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;

        public SceneAuthorizationController(Management.ManagementClientAccessor accessor, IEnvironment environment, IUserSessions sessions, ILogger logger)
        {
            _logger = logger;
            _accessor = accessor;
          
            _sessions = sessions;
            _environment = environment;
        }
        public async Task GetToken(RequestContext<IScenePeerClient> ctx)
        {
            

            var client = await _accessor.GetApplicationClient();

            var user = await _sessions.GetUser(ctx.RemotePeer);
            if (user == null)
            {
                throw new ClientException("userDisconnected");
            }
            var sceneId = ctx.ReadObject<string>();
           
            var token = await client.CreateConnectionToken(sceneId, new byte[0], "application/octet-stream");

            await ctx.SendValue(token);
        }

        public async Task GetBearerToken(RequestContext<IScenePeerClient> ctx)
        {
            var sessionId = ctx.RemotePeer.SessionId;
            var app = await _environment.GetApplicationInfos();
            var session = await _sessions.GetSessionById(sessionId);
            var token = TokenGenerator.CreateToken(new BearerTokenData { SessionId = sessionId, pid = session.platformId, userId = session.User.Id, IssuedOn = DateTime.UtcNow, ValidUntil = DateTime.UtcNow + TimeSpan.FromHours(1) }, app.PrimaryKey);
            await ctx.SendValue(token);
        }
       
        public async Task GetUserFromBearerToken(RequestContext<IScenePeerClient> ctx)
        {
            var app = await _environment.GetApplicationInfos();
            var bearerToken = ctx.ReadObject<string>();

            if (string.IsNullOrEmpty(bearerToken))
            {
                _logger.Log(LogLevel.Error, "authorization", $"Missing bearer token when calling GetUserFromBearerToken()", new { });
            }
            var data = TokenGenerator.DecodeToken<BearerTokenData>(bearerToken, app.PrimaryKey);
            if (data == null)
            {
                throw new ClientException("Invalid Token");
            }

            if (string.IsNullOrEmpty(data?.userId))
            {
                _logger.Log(LogLevel.Error, "authorization", $"Failed to retrieve userId from bearer token data", new { data });
            }
            await ctx.SendValue(data?.userId);
        }
    }
}
