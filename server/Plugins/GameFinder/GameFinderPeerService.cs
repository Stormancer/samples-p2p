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
//using Stormancer.Server.Users;
//using Stormancer;
//using Stormancer.Core;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Stormancer.Server;

//namespace Stormancer.Server.GameFinder
//{
//    public class GameFinderPeerService
//    {
//        private readonly IUserSessions _sessions;
//        private readonly ISceneHost _scene;

//        public GameFinderPeerService(IUserSessions sessions, ISceneHost scene)
//        {
//            _scene = scene;
//            _sessions = sessions;
//        }

//        public Task<IScenePeerClient> GetPlayer(Player member)
//        {
//            return _sessions.GetPeer(member.UserId);
//        }

//        private async Task<IEnumerable<IScenePeerClient>> GetPlayers(Group group)
//        {
//            return await Task.WhenAll(group.Players.Select(GetPlayer));
//        }

//        public async Task<IEnumerable<IScenePeerClient>> GetPlayers(params Group[] groups)
//        {
//            return await Task.WhenAll(groups.SelectMany(g => g.Players).Select(GetPlayer));
//        }

//        public Task BroadcastToPlayers(Group group, string route, Action<System.IO.Stream, ISerializer> writer)
//        {
//            return BroadcastToPlayers(new Group[] { group }, route, writer);
//        }

//        public async Task BroadcastToPlayers(IEnumerable<Group> groups, string route, Action<System.IO.Stream, ISerializer> writer)
//        {
//            var peers = await GetPlayers(groups.ToArray());
//            foreach (var group in peers.Where(p => p != null).GroupBy(p => p.Serializer()))
//            {
//                _scene.Send(new MatchArrayFilter(group), route, s => writer(s, group.Key), PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
//            }
//        }
//    }
//}

