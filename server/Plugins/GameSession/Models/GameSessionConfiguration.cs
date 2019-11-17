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
using System.Collections.Generic;
using System.Linq;
using MsgPack.Serialization;
using Newtonsoft.Json.Linq;

namespace Stormancer.Server.GameSession
{
    public class GameSessionConfiguration
    {
        /// <summary>
        /// True if anyone can connect to the game session.
        /// </summary>
        [MessagePackMember(0)]
        public bool Public { get; set; }

        [MessagePackMember(1)]
        public bool canRestart { get; set; }

        /// <summary>
        /// User id of the game host. In party the host user id value is the party leader.
        /// </summary>
        [MessagePackMember(2)]
        public string HostUserId { get; set; }

        /// <summary>
        /// Group connected to gameSession
        /// </summary>
        [MessagePackMember(3)]
        public List<Team> Teams { get; set; } = new List<Team>();

        /// <summary>
        /// Get the User ID of every player in Teams
        /// </summary>
        [MessagePackMember(4)]
        public IEnumerable<string> UserIds { get => Teams.SelectMany(t => t.PlayerIds); }

        /// <summary>
        /// Gamesession parameters like map to launch, gameType and everything can be useful to 
        /// dedicated server.
        /// </summary>
        [MessagePackMember(5)]
        public JObject Parameters { get; set; } = new JObject();
    }
}
