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
using System.Collections.Concurrent;
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
                b.Register<ManagementClientAccessor>().SingleInstance();
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
        private Lazy<Task<ApplicationClient>> _client;
        private ConcurrentDictionary<string, Task<ApplicationClient>> _clients = new ConcurrentDictionary<string, Task<ApplicationClient>>();

        private Lazy<Task<FederationViewModel>> _federation;
        public ManagementClientAccessor(IEnvironment environment, IConfiguration config, ILogger logger)
        {
            _environment = environment;
            _logger = logger;
            config.SettingsChanged += (o, settings) => ApplyConfig(settings);
            ApplyConfig(config.Settings);

            _client = new Lazy<Task<ApplicationClient>>(async () =>
            {

                var infos = await _environment.GetApplicationInfos();

                var result = Stormancer.Management.Client.ApplicationClient.ForApi(infos.AccountId, infos.ApplicationName, infos.PrimaryKey);
                result.Endpoint = infos.ApiEndpoint;
                return result;
            });
            ResetFederation();
        }

        private void ResetFederation()
        {
            _federation = new Lazy<Task<FederationViewModel>>(() => _environment.GetFederation());
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
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "manage", $"Failed to create the scene {sceneUri} on {client.Endpoint}", ex);
                throw;
            }
        }

        private async Task<(ApplicationClient, string)> GetClientForSceneUri(string sceneUri)
        {

            (var clusterId, var account, var app, var sceneId) = ParseSceneUri(sceneUri);
            var federation = await (_federation.Value);
            if (clusterId == null)
            {
                clusterId = federation.current.id;
            }
            try
            {
                var result = await _clients.GetOrAdd(clusterId, (id) => getClientForSceneUriImpl(clusterId, account, app, sceneId));
                return (result,sceneId);
            }
            catch
            {
                _clients.TryRemove(clusterId, out _);
                throw;
            }

        }

        private async Task<ApplicationClient> getClientForSceneUriImpl(string clusterId, string account, string app, string sceneId)
        {

            var federation = await (_federation.Value);
            var appInfos = await _environment.GetApplicationInfos();


            if (app == null)
            {
                app = appInfos.ApplicationName;
            }
            if (account == null)
            {
                account = appInfos.AccountId;
            }
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
            if (endpoints == null) //Maybe outdated federation?
            {
                ResetFederation();//Refresh and retry.
                federation = await _federation.Value;
                endpoints = federation.clusters.FirstOrDefault(c => c.id == clusterId)?.endpoints;
                if (endpoints == null)//No luck Error.
                {
                    throw new ClientException($"notFound?cluster={clusterId}");
                }
            }
            if (primaryKey == null)
            {
                throw new ClientException("notFound?keys");
            }

            var client = Stormancer.Management.Client.ApplicationClient.ForApi(account, app, primaryKey, endpoints.RandomElement());
            return client;
        }
        private (string, string, string, string) ParseSceneUri(string uri)
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


            return (clusterId, account, application, sceneId);
        }

        public Task<Stormancer.Management.Client.ApplicationClient> GetApplicationClient()
        {
            return _client.Value;
        }
    }
}
