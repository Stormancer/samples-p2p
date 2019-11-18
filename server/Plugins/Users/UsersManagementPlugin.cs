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
using Server.Plugins.AdminApi;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server.Analytics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stormancer.Server.Components;
using Stormancer.Plugins.ServiceLocator;

namespace Stormancer.Server.Users
{
    public class UserManagementConfig
    {
        public void AddAuthenticationProvider<TProvider>() where TProvider : IAuthenticationProvider
        {
            EnabledAuthenticationProviders.Add(typeof(TProvider));
        }
        public List<Type> EnabledAuthenticationProviders { get; } = new List<Type>();
    }
    class UsersManagementPlugin : Stormancer.Plugins.IHostPlugin
    {
        public const string SCENE_TEMPLATE = "authenticator";


        public static string GetSceneId()
        {
            return SCENE_TEMPLATE;
        }

        public UsersManagementPlugin()
        {

        }

        public void Build(HostPluginBuildContext ctx)
        {
            ctx.HostStarting += HostStarting;
            ctx.HostStarted += HostStarted;
            ctx.HostDependenciesRegistration += RegisterDependencies;
            ctx.SceneDependenciesRegistration += RegisterSceneDependencies;
            ctx.SceneCreated += SceneCreated;
            ctx.SceneStarted += (ISceneHost scene) =>
            {
                if (scene.Template == SCENE_TEMPLATE)
                {
                    // Push authenticated users count
                    scene.RunTask(async ct =>
                    {
                        var analytics = scene.DependencyResolver.Resolve<IAnalyticsService>();
                        var sessions = scene.DependencyResolver.Resolve<IUserSessions>() as UserSessions;
                        var logger = scene.DependencyResolver.Resolve<ILogger>();
                        while (!ct.IsCancellationRequested)
                        {
                            var authenticatedUsersCount = sessions?.AuthenticatedUsersCount ?? 0;
                            analytics.Push("user", "sessions", JObject.FromObject(new { AuthenticatedUsersCount = authenticatedUsersCount }));
                            await Task.Delay(1000);
                        }
                    });
                    // Start periodic users credentials renewal
                    scene.RunTask(ct => scene.DependencyResolver.Resolve<CredentialsRenewer>().PeriodicRenewal(ct));
                }
            };
        }

        private void SceneCreated(ISceneHost scene)
        {
            if (scene.Template != SCENE_TEMPLATE)
            {
                var index = scene.DependencyResolver.Resolve<UserSessionCache>();
                scene.Connected.Add(index.OnConnected, 1000);
                scene.Disconnected.Add(args => index.OnDisconnected(args.Peer));
            }
        }

        private void RegisterSceneDependencies(IDependencyBuilder b, ISceneHost scene)
        {
            if (scene.Template == SCENE_TEMPLATE)
            {
                b.Register<UserSessions>().As<IUserSessions>();
                b.Register<UserPeerIndex>().As<IUserPeerIndex>().SingleInstance();
                b.Register<PeerUserIndex>().As<IPeerUserIndex>().SingleInstance();
                b.Register<DeviceIdentifierAuthenticationProvider>().As<IAuthenticationProvider>();
                b.Register<AdminImpersonationAuthenticationProvider>().As<IAuthenticationProvider>();
                b.Register<CredentialsRenewer>().AsSelf().As<IAuthenticationEventHandler>().SingleInstance();

            }
            else
            {
                
                b.Register<UserSessionsProxy>(dr => new UserSessionsProxy(
                      dr.Resolve<ISceneHost>(),
                      dr.Resolve<ISerializer>(),
                      dr.Resolve<IEnvironment>(),
                      dr.Resolve<IServiceLocator>(),
                      dr.Resolve<UserSessionCache>()))
                   .As<IUserSessions>().InstancePerRequest();
            }
        }

        private void HostStarted(IHost host)
        {
            var managementAccessor = host.DependencyResolver.Resolve<Management.ManagementClientAccessor>();
            if (managementAccessor != null)
            {
                managementAccessor.GetApplicationClient().ContinueWith(async t =>
                {
                    var client = await t;
                    await client.CreateScene(GetSceneId(), SCENE_TEMPLATE, true);
                });
            }
        }

        private void RegisterDependencies(IDependencyBuilder b)
        {
            //Indices

            //b.Register<UserToGroupIndex>().SingleInstance();
            //b.Register<GroupsIndex>().SingleInstance();
            //b.Register<SingleNodeActionStore>().As<IActionStore>().SingleInstance();
            b.Register<SceneAuthorizationController>();
            b.Register<UserSessionController>();
            b.Register<AuthenticationController>().InstancePerRequest();
            b.Register<AuthenticationService>().As<IAuthenticationService>().InstancePerRequest();

            b.Register<UserService>().As<IUserService>();

            b.Register<UserManagementConfig>().SingleInstance();
            b.Register<UsersAdminController>();
            b.Register<AdminWebApiConfig>().As<IAdminWebApiConfig>();

            b.Register<UserSessionCache>().AsSelf().InstancePerScene();
            b.Register<PlatformSpecificServices>().As<IPlatformSpecificServices>();
        }

        private void HostStarting(IHost host)
        {
            host.AddSceneTemplate(SCENE_TEMPLATE, AuthenticatorSceneFactory);
        }

        private void AuthenticatorSceneFactory(ISceneHost scene)
        {
          



            //scene.AddController<GroupController>();
            scene.AddController<AuthenticationController>();
            scene.AddController<SceneAuthorizationController>();
            scene.AddController<UserSessionController>();




        }


    }
}

