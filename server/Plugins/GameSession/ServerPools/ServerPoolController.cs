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

using MsgPack.Serialization;
using Server.Plugins.API;
using Stormancer.Core;
using Stormancer.Server.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.GameSession
{
    public class GameServerStartupParameters
    {
        [MessagePackMember(0)]
        public string GameSessionConnectionToken { get; set; }

        [MessagePackMember(1)]
        public GameSessionConfiguration Config { get; set; }
        public string GameSessionId { get; internal set; }
    }
    class ServerPoolController : ControllerBase
    {
        private readonly ServerPools pools;
        private readonly IUserSessions sessions;

        public ServerPoolController(ServerPools pools, IUserSessions sessions)
        {
            this.pools = pools;
            this.sessions = sessions;
        }

        protected internal override Task OnDisconnected(DisconnectedArgs args)
        {
            pools.SetShutdown(args.Peer.SessionId);
            return Task.CompletedTask;
        }

        [Api(ApiAccess.Public, ApiType.Rpc)]
        public async Task<GameServerStartupParameters> SetReady()
        {
            var session = await sessions.GetSession(this.Request.RemotePeer);
            if(session == null)
            {
                throw new ClientException("session.notFound");
            }
            if(session.platformId.Platform != DedicatedServerAuthProvider.PROVIDER_NAME)
            {
                throw new ClientException("serverPool.notAuthenticatedAsDedicatedServer");
            }

            return await pools.SetReady(session.platformId.OnlineId,this.Request.RemotePeer);
        }
    }
}
