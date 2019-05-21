using Stormancer;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using Stormancer.Server;
using Stormancer.Server.Components;
using Stormancer.Server.GameFinder;
using Stormancer.Server.Users;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace P2p
{
    public class App : IStartup
    {
        public void Run(IAppBuilder builder)
        {
            builder.AddPlugin(new P2pPlugin());
        }
    }


    public class P2pPlugin : IHostPlugin
    {
        public const string GAMESESSION_TEMPLATE = "gamesession";
        public const string GAMEFINDER_TEMPLATE = "gamefinder";

        public void Build(HostPluginBuildContext ctx)
        {
            ctx.HostStarting += HostStarting;
            ctx.HostStarted += HostStarted;
        }

        private void HostStarting(IHost host)
        {
           
            host.AddSceneTemplate(GAMEFINDER_TEMPLATE, (ISceneHost scene)=>{

                scene.AddGameFinder(new Stormancer.Server.GameFinder.GameFinderConfig("default",builder=> {
                    builder.Register<SampleGameFindingResolver>().As<IGameFinderResolver>();
                    builder.Register<SampleGameFinder>().As<IGameFinder>();
                    builder.Register<SampleGameFinderDataExtractor>().As<IGameFinderDataExtractor>();
                }));

            });
            host.AddSceneTemplate(GAMESESSION_TEMPLATE, (ISceneHost scene) => {

                scene.AddGameSession();
            });
        }

        private void HostStarted(IHost host)
        {
            var managementAccessor = host.DependencyResolver.Resolve<Stormancer.Server.Management.ManagementClientAccessor>();
            if (managementAccessor != null)
            {
                managementAccessor.GetApplicationClient().ContinueWith(async t =>
                {
                    var client = await t;
                    await client.CreateScene("gamefinder-default", GAMEFINDER_TEMPLATE, true);
                });
            }
        }
    }

}
