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
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Stormancer.Server.GameFinder
{
    public class Game
    {
        #region constructors
        public Game(JObject customData = null)
            : this(0, customData)
        {
        }

        public Game(params Team[] teams) : this(teams, null)
        {
        }

        public Game(int teamCount, JObject customData = null) 
            : this(teamCount, () => new Team(), customData)
        {
        }

        public Game(int teamCount, Func<Team> teamFactory, JObject customData = null)
        {
            CustomData = customData;
            for(var i = 0; i< teamCount; i++)
            {
                Teams.Add(teamFactory());
            }
            Id = Guid.NewGuid().ToString();
        }

        public Game(IEnumerable<Team> teams, JObject customData = null)
        {
            CustomData = customData;
            Teams.AddRange(teams);
            Id = Guid.NewGuid().ToString();
        }
        public static Game Create<T>(IEnumerable<Team> teams, T customData = null) where T:class
        {
            return new Game(teams, customData != null ? JObject.FromObject(customData) : null);
        }
        #endregion

        public string Id { get; set; }

        public JObject CustomData { get; set; }

        public object CommonCustomData { get; set; }

        public List<Team> Teams { get; set; } = new List<Team>();

        public IEnumerable<Group> AllGroups => Teams.SelectMany(team => team.Groups);

        public IEnumerable<Player> AllPlayers => Teams.SelectMany(team => team.AllPlayers);
    }
}
