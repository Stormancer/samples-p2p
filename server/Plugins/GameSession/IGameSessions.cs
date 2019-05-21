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
using Stormancer.Server.Management;
using Stormancer.Server.Users;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.GameSession
{
    public interface IGameSessions
    {
        Task Create(string template, string id, GameSessionConfiguration config);
        Task<string> CreateConnectionToken(string id, string userSessionId);
    }

    internal class GameSessions : IGameSessions
    {
        private readonly ManagementClientAccessor management;
        private readonly IUserSessions sessions;
        private readonly ISerializer serializer;

        public GameSessions(ManagementClientAccessor management, IUserSessions sessions, ISerializer serializer)
        {
            this.management = management;
            this.sessions = sessions;
            this.serializer = serializer;
        }
        public Task Create(string template, string id, GameSessionConfiguration config)
        {
            return management.CreateScene(id, template, false, false, JObject.FromObject(new { gameSession = config }));
        }

        public async Task<string> CreateConnectionToken(string id, string userSessionId)
        {

            using (var stream = new MemoryStream())
            {
                var session = await sessions.GetSessionById(userSessionId);
                serializer.Serialize(session, stream);
                return await TaskHelper.Retry(async () =>
                {
                    return await management.CreateConnectionToken(id, stream.ToArray(), "stormancer/userSession");

                }, RetryPolicies.IncrementalDelay(4, TimeSpan.FromSeconds(200)), CancellationToken.None, ex => true);
            }

        }
    }
}
