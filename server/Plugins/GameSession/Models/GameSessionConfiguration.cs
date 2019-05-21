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
using Newtonsoft.Json.Linq;

namespace Stormancer.Server.GameSession
{
    public class GameSessionConfiguration
    {
        /// <summary>
        /// List of users can connect to gameSession
        /// (sessionId=>userId)
        /// </summary>
        public Dictionary<string,string> userIds { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// True if anyone can connect to the game session.
        /// </summary>
        public bool Public { get; set; }

        public bool canRestart { get; set; }

        /// <summary>
        /// User id of the game host. In party the host user id value is the party leader.
        /// </summary>
        public string HostUserId { get; set; }

        /// <summary>
        /// Group connected to gameSession
        /// </summary>
        public List<Team> Teams { get; set; } = new List<Team>();

        /// <summary>
        /// Gamesession parameters like map to launch, gameType and everything can be useful to 
        /// dedicated server.
        /// </summary>
        public JObject Parameters { get; set; } = new JObject();
    }
}
