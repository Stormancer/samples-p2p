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
using Server.Plugins.API;
using Server.Plugins.Configuration;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server.Users;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Stormancer.Server.Analytics
{
    public class InstrumentationConfig
    {
        public bool EnableApiInstrumentation { get; set; } = false;
    }
    public class AnalyticsEventHandler : IUserSessionEventHandler, IApiHandler
    {
        private readonly IAnalyticsService _analytics;
        private readonly ILogger _logger;
        private readonly Stopwatch _watch = new Stopwatch();
        private InstrumentationConfig _config;


        private void ApplySettings(dynamic config)
        {
            _config = (InstrumentationConfig)(config?.instrumentation?.ToObject<InstrumentationConfig>()) ?? new InstrumentationConfig();
        }

        public AnalyticsEventHandler(IAnalyticsService analytics, ILogger logger, IConfiguration configuration)
        {
            _analytics = analytics;
            _watch.Start();
            configuration.SettingsChanged += (_, settings) => ApplySettings(settings);
            ApplySettings(configuration.Settings);
            _logger = logger;
        }

        public Task OnLoggedIn(LoginContext loginCtx)
        {
            _analytics.Push("user", "login", JObject.FromObject(new { UserId = loginCtx.Session.User.Id, PlatformId = loginCtx.Session.platformId }));
            return Task.CompletedTask;
        }

        public Task OnLoggedOut(LogoutContext logoutCtx)
        {
            _analytics.Push("user", "logout", JObject.FromObject(new { UserId = logoutCtx.Session.User.Id, logoutCtx.ConnectedOn, duration = (DateTime.UtcNow - logoutCtx.ConnectedOn).TotalSeconds }));
            return Task.CompletedTask;
        }


        public async Task RunRpc(ApiCallContext<RequestContext<IScenePeerClient>> ctx, Func<ApiCallContext<RequestContext<IScenePeerClient>>, Task> next)
        {
            if (_config.EnableApiInstrumentation)
            {
                var start = _watch.ElapsedMilliseconds;
                await next(ctx);
                _analytics.Push("api", "rpc.cs", JObject.FromObject(new
                {
                    type = "RPC",
                    scope = "ClientServer",
                    inputSize = ctx.Context.InputStream.Length,
                    route = ctx.Route,
                    duration = _watch.ElapsedMilliseconds - start,
                    SessionId = ctx.Context.RemotePeer.SessionId
                }));
            }
            else
            {
                await next(ctx);
            }


        }

        public async Task RunRpc(ApiCallContext<RequestContext<IScenePeer>> ctx, Func<ApiCallContext<RequestContext<IScenePeer>>, Task> next)
        {
            if (_config.EnableApiInstrumentation)
            {
                var start = _watch.ElapsedMilliseconds;
                await next(ctx);
                _analytics.Push("api", "rpc.s2s", JObject.FromObject(new
                {
                    type = "RPC",
                    scope = "S2S",
                    inputSize = ctx.Context.InputStream.Length,
                    route = ctx.Route,
                    duration = _watch.ElapsedMilliseconds - start,
                    SessionId = ctx.Context.RemotePeer.SceneId
                }));

            }
            else
            {
                await next(ctx);
            }
        }

        public async Task RunFF(ApiCallContext<Packet<IScenePeer>> ctx, Func<ApiCallContext<Packet<IScenePeer>>, Task> next)
        {
            if (_config.EnableApiInstrumentation)
            {
                var start = _watch.ElapsedMilliseconds;
                await next(ctx);
                _analytics.Push("api", "ff.s2s", JObject.FromObject(new
                {
                    type = "FireForget",
                    scope = "S2S",
                    inputSize = ctx.Context.Stream.Length,
                    route = ctx.Route,
                    duration = _watch.ElapsedMilliseconds - start,
                    SessionId = ctx.Context.Connection.SceneId
                }));

            }
            else
            {
                await next(ctx);
            }
        }

        public async Task RunFF(ApiCallContext<Packet<IScenePeerClient>> ctx, Func<ApiCallContext<Packet<IScenePeerClient>>, Task> next)
        {
            if (_config.EnableApiInstrumentation)
            {
                var start = _watch.ElapsedMilliseconds;
                await next(ctx);
                _analytics.Push("api", "ff.cs", JObject.FromObject(new
                {
                    type = "FireForget",
                    scope = "ClientServer",
                    inputSize = ctx.Context.Stream.Length,
                    route = ctx.Route,
                    duration = _watch.ElapsedMilliseconds - start,
                    SessionId = ctx.Context.Connection.SessionId
                }));

            }
            else
            {
                await next(ctx);
            }
        }

        public Task OnConnected(IScenePeerClient client)
        {
            return Task.CompletedTask;
        }

        public Task OnDisconnected(IScenePeerClient client)
        {
            return Task.CompletedTask;
        }
    }
}

