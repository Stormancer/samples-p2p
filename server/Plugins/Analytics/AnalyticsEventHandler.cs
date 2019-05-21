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
using Stormancer.Diagnostics;
using Stormancer.Server.GameFinder;
using Stormancer.Server.Users;
using System.Linq;
using System.Threading.Tasks;

namespace Stormancer.Server.Analytics
{
    public class AnalyticsEventHandler : IGameFinderEventHandler, IUserSessionEventHandler
    {
        private readonly IAnalyticsService _analytics;
        private readonly ILogger _logger;

        public AnalyticsEventHandler(IAnalyticsService analytics, ILogger logger)
        {
            _analytics = analytics;
            _logger = logger;
        }

        public Task OnLoggedIn(LoginContext loginCtx)
        {
            _analytics.Push("user-login", JObject.FromObject(new { UserId = loginCtx.Session.User.Id, PlatformId = loginCtx.Session.platformId }));
            return Task.CompletedTask;
        }

        public Task OnLoggedOut(LogoutContext logoutCtx)
        {
            _analytics.Push("user-logout", JObject.FromObject(new { UserId = logoutCtx.Session.User.Id, ConnnectedOn = logoutCtx.ConnectedOn }));
            return Task.CompletedTask;
        }

        public Task OnStart(SearchStartContext searchStartCtx)
        {
            foreach (var group in searchStartCtx.Groups)
            {
                _analytics.Push("gamefinder-start", JObject.FromObject(new { searchStartCtx.GameFinderId, players = group.Players.Count() }));
            }
            return Task.CompletedTask;
        }

        public Task OnEnd(SearchEndContext searchEndCtx)
        {
            _analytics.Push("gamefinder-end", JObject.FromObject(new { searchEndCtx.GameFinderId, searchEndCtx.Reason, searchEndCtx.PassesCount }));
            return Task.CompletedTask;
        }

        public Task OnGameStarted(GameStartedContext searchGameStartedCtx)
        {
            _analytics.Push("gamefinder-start", JObject.FromObject(new { searchGameStartedCtx.GameFinderId, playerCount = searchGameStartedCtx.Game.AllPlayers.Count() }));
            return Task.CompletedTask;
        }
    }
}
