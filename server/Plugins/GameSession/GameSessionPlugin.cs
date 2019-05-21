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
using Stormancer.Core;
using Stormancer.Plugins;
using Stormancer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Server.Plugins.API;
using Stormancer.Server;
using Stormancer.Server.Users;
using Stormancer.Server.Components;
using Server.Plugins.Configuration;
using Stormancer.Platform.Core.Cryptography;
using System.Threading;

namespace Stormancer.Server.GameSession
{
    class GameSessionPlugin : IHostPlugin
    {
        public const string METADATA_KEY = "stormancer.gamesession";

        public void Build(HostPluginBuildContext ctx)
        {
            ctx.SceneDependenciesRegistration += (IDependencyBuilder builder, ISceneHost scene) =>
            {
                if (scene.Metadata.ContainsKey(METADATA_KEY))
                {
                    builder.Register<GameSessionService>().As<IGameSessionService>().SingleInstance();
                    builder.Register<GameSessionController>().InstancePerRequest();
                }
                if (scene.Template == UsersManagementPlugin.SCENE_TEMPLATE)
                {
                    builder.Register<DedicatedServerAuthProvider>().As<IAuthenticationProvider>();
                }
                builder.Register<GameSessions>().As<IGameSessions>();
            };
            ctx.HostStarted += (IHost host) =>
            {

            };
            ctx.SceneCreated += (ISceneHost scene) =>
            {
                if (scene.Metadata.ContainsKey(METADATA_KEY))
                {
                    scene.AddController<GameSessionController>();

                    scene.Starting.Add(metadata =>
                    {
                        
                        var service = scene.DependencyResolver.Resolve<IGameSessionService>();
                        service.SetConfiguration(metadata);
                        service.TryStart();
                        return Task.FromResult(true);

                    });
                }
            };
        }
    }
    class DedicatedServerAuthProvider : IAuthenticationProvider
    {
        private const string PROVIDER_NAME = "dedicatedServer";
        private IEnvironment _env;

        public string Type => PROVIDER_NAME;
        public DedicatedServerAuthProvider(IEnvironment env)
        {
            _env = env;
        }

        public void AddMetadata(Dictionary<string, string> result)
        {
            //result.Add("provider.dedicatedServer", "enabled");
        }

        public async Task<AuthenticationResult> Authenticate(AuthenticationContext authenticationCtx, CancellationToken ct)
        {


            var token = authenticationCtx.Parameters["token"];
            var appInfos = await _env.GetApplicationInfos();

            try
            {
                var gameId = TokenGenerator.DecodeToken<string>(token, appInfos.PrimaryKey);


                return AuthenticationResult.CreateSuccess(new User { Id = "ds-" + gameId }, new PlatformId { OnlineId = gameId, Platform = PROVIDER_NAME }, authenticationCtx.Parameters);

            }
            catch (Exception ex)
            {
                return AuthenticationResult.CreateFailure("Invalid token :" + ex.ToString(), new PlatformId { Platform = PROVIDER_NAME }, authenticationCtx.Parameters);
            }
        }

        public Task Setup(Dictionary<string, string> parameters)
        {
            throw new NotImplementedException();
        }
    }
}
