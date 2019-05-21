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
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using System;
using System.Collections.Generic;

namespace Stormancer.Server.GameFinder
{
    public class GameFinderPlugin : IHostPlugin
    {
        public const string METADATA_KEY = "stormancer.plugins.gamefinder";

        internal static Dictionary<string, GameFinderConfig> Configs = new Dictionary<string, GameFinderConfig>();

        public void Build(HostPluginBuildContext ctx)
        {
            //ctx.HostStarting += HostStarting;
            ctx.SceneDependenciesRegistration += SceneDependenciesRegistration;
            ctx.SceneCreated += SceneCreated;
        }

        private void SceneDependenciesRegistration(IDependencyBuilder builder, ISceneHost scene)
        {
            string kind;
            if (scene.Metadata.TryGetValue(METADATA_KEY, out kind))
            {
                GameFinderConfig config;
                if (Configs.TryGetValue(kind, out config))
                {
                    config.RegisterDependencies(builder);
                }
            }
            builder.Register<GameFinderController>();
            builder.Register<GameFinderService>().As<IGameFinderService>().InstancePerScene();
        }

        private void SceneCreated(ISceneHost scene)
        {
            string kind;
            if (scene.Metadata.TryGetValue(METADATA_KEY, out kind))
            {
                scene.AddController<GameFinderController>();
                var logger = scene.DependencyResolver.Resolve<ILogger>();
                try
                {
                    var gameFinderService = scene.DependencyResolver.Resolve<IGameFinderService>();       

                    //Start gameFinder
                    scene.RunTask(gameFinderService.Run);

                }
                catch (Exception ex)
                {
                    logger.Log(LogLevel.Error, "plugins.gameFinder", $"An exception occured when creating scene {scene.Id}.", ex);
                    throw;
                }
            }
        }
    }
}
