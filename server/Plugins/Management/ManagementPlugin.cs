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
using Server.Plugins.Configuration;
using Stormancer.Diagnostics;
using Stormancer.Management.Client;
using Stormancer.Plugins;
using Stormancer.Server.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Stormancer.Server.Management
{
    public class Startup
    {
        public void Run(IAppBuilder builder)
        {
            builder.AddPlugin(new ManagementPlugin());
        }
    }
    class ManagementPlugin : IHostPlugin
    {
        public void Build(HostPluginBuildContext ctx)
        {
            ctx.HostDependenciesRegistration += (IDependencyBuilder b) =>
              {
                  b.Register<ManagementClientAccessor>();
              };
        }
    }
    public class ManagementClientConfig
    {

        /// <summary>
        /// Access keys used to manage applications associated with this app. {clusterId}/{account}/{app}=>accessKey
        /// </summary>
        public Dictionary<string, string> AccessKeys { get; set; } = new Dictionary<string, string>();
    }
    public class ManagementClientAccessor
    {
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private ManagementClientConfig _config;

        public ManagementClientAccessor(IEnvironment environment, IConfiguration config, ILogger logger)
        {
            _environment = environment;
            _logger = logger;
            config.SettingsChanged += (o, settings) => ApplyConfig(settings);
            ApplyConfig(config.Settings);
        }
        private void ApplyConfig(dynamic settings)
        {
            _config = ((JObject)settings.management)?.ToObject<ManagementClientConfig>() ?? new ManagementClientConfig();
        }

        public async Task<string> CreateConnectionToken(string sceneUri, byte[] payload = null, string contentType = "application/octet-stream")
        {

            (var client, var sceneId) = await GetClientForSceneUri(sceneUri);
            return await client.CreateConnectionToken(sceneId, payload ?? new byte[0], contentType);

        }
        public async Task CreateScene(string sceneUri, string template, bool isPublic, bool isPersistent, JObject metadata = null)
        {
            
            (var client, var sceneId) = await GetClientForSceneUri(sceneUri);
            try
            {
                await client.CreateScene(sceneId, template, isPublic, metadata, isPersistent);
            }
            catch(Exception ex)
            {
                _logger.Log(LogLevel.Error,"manage", $"Failed to create the scene {sceneUri} on {client.Endpoint}", ex);
                throw;
            }
        }

        private async Task<(ApplicationClient, string)> GetClientForSceneUri(string sceneUri)
        {
            var federation = await _environment.GetFederation();
            var appInfos = await _environment.GetApplicationInfos();
            (var clusterId, var account, var app, var sceneId) = ParseSceneUri(sceneUri, federation.current.id, appInfos.AccountId, appInfos.ApplicationName);

            IEnumerable<string> endpoints = null;
            string primaryKey = null;
            if (clusterId == federation.current.id)
            {
                endpoints = federation.current.endpoints;
                if (account == appInfos.AccountId && app == appInfos.ApplicationName)
                {
                    primaryKey = appInfos.PrimaryKey;
                }

            }
            else
            {
                endpoints = federation.clusters.FirstOrDefault(c => c.id == clusterId)?.endpoints;
            }

            if (primaryKey == null)
            {
                _config.AccessKeys.TryGetValue($"{clusterId}/{account}/{app}", out primaryKey);
            }
            if (endpoints == null)
            {
                throw new ClientException("notFound?cluster");
            }
            if (primaryKey == null)
            {
                throw new ClientException("notFound?keys");
            }

            var client = Stormancer.Management.Client.ApplicationClient.ForApi(account, app, primaryKey, endpoints.RandomElement());
            return (client, sceneId);
        }



        private Tuple<string, string, string, string> ParseSceneUri(string uri, string defaultClusterId, string defaultAccountId, string defaultApplication)
        {
            string clusterId = null, account = null, application = null, sceneId = null;

            if (uri.ToLowerInvariant().StartsWith("scene:"))
            {
                var segments = uri.Split('/');
                sceneId = segments[segments.Length - 1];
                //scene:/app/scene
                if (segments.Length > 2)
                {
                    application = segments[segments.Length - 2];

                }
                //scene:/account/app/scene
                if (segments.Length > 3)
                {
                    account = segments[segments.Length - 3];
                }

                //scene:/clusterId/account/app/scene
                if (segments.Length > 4)
                {
                    clusterId = segments[segments.Length - 4];
                }
            }
            else
            {
                sceneId = uri;
            }

            if (string.IsNullOrEmpty(clusterId))
            {
                clusterId = defaultClusterId;
            }
            if (string.IsNullOrEmpty(account))
            {
                account = defaultAccountId;
            }
            if (string.IsNullOrEmpty(application))
            {
                application = defaultApplication;
            }

            return Tuple.Create(clusterId, account, application, sceneId);
        }

        public async Task<Stormancer.Management.Client.ApplicationClient> GetApplicationClient()
        {
            var infos = await _environment.GetApplicationInfos();

            var result = Stormancer.Management.Client.ApplicationClient.ForApi(infos.AccountId, infos.ApplicationName, infos.PrimaryKey);
            result.Endpoint = infos.ApiEndpoint;
            return result;
        }
    }
}
