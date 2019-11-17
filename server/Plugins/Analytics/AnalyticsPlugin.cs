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

using Server.Plugins.AdminApi;
using Server.Plugins.API;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server.Users;
using System;
using System.Threading.Tasks;

namespace Stormancer.Server.Analytics
{
    class AnalyticsPlugin : IHostPlugin
    {
        internal const string METADATA_KEY = "stormancer.analytics";

        public void Build(HostPluginBuildContext ctx)
        {
            ctx.HostDependenciesRegistration += (IDependencyBuilder builder) =>
              {
                  builder.Register<AnalyticsController>().InstancePerRequest();
                  builder.Register<AnalyticsService>().As<IAnalyticsService>().SingleInstance();
                  //builder.Register<AnalyticsEventHandler>().As<IUserSessionEventHandler>().As<IApiHandler>().SingleInstance();
              };

            ctx.HostStarted += (IHost host) =>
            {
                Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            await Task.Delay(1000 * 5);
                            var analyticsService = host.DependencyResolver.Resolve<IAnalyticsService>();
                            await analyticsService.Flush();                          
                        }
                        catch (Exception ex)
                        {
                            host.DependencyResolver.Resolve<ILogger>().Log(LogLevel.Error, "analytics", "failed to push analytics", ex);
                        }
                    }

                });
            };

            ctx.SceneCreated += (ISceneHost scene) =>
             {
                 if (scene.Metadata.ContainsKey(METADATA_KEY))
                 {
                     scene.AddController<AnalyticsController>();
                 }
             };
        }
    }
}

