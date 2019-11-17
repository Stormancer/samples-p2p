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
using System.Collections.Generic;
using System.Linq;

namespace Stormancer.Server.GameSession
{
    public class Team
    {
        public Team(params Group[] groups)
        {
            Groups = new List<Group>(groups);
        }

        public Team(IEnumerable<Group> groups)
        {
            Groups = new List<Group>(groups);
        }

        public Team()
        {
            // Default ctor for backward compatibility
        }

        [MessagePackMember(0)]
        public string TeamId { get; set; }
        [MessagePackMember(1)]
        public List<Group> Groups { get; set; } = new List<Group>();

        [MessagePackMember(2)]
        public IEnumerable<string> PlayerIds { get => Groups.SelectMany(group => group.PlayersId); }
    }
}
