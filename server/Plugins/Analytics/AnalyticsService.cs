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
using Stormancer.Server.Components;
using Stormancer.Server.Database;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.Analytics
{
    class AnalyticsService : IAnalyticsService
    {
        private const string LOG_CATEGORY = "Analytics";
        private const string TABLE_NAME = "analytics";
        private readonly ConcurrentDictionary<string, ConcurrentQueue<AnalyticsDocument>> _documents = new ConcurrentDictionary<string, ConcurrentQueue<AnalyticsDocument>>();
        private readonly IESClientFactory _cFactory;
        private readonly IEnvironment _environment;
        private readonly Task<ApplicationInfos> _applicationInfosTask;
        private readonly Task<FederationViewModel> _federation;

        
        private ILogger _logger;

        public AnalyticsService(
            IESClientFactory clientFactory,
            IEnvironment environment,
            ILogger logger,
            IConfiguration configuration)
        {
            _cFactory = clientFactory;
            _environment = environment;
            _logger = logger;
            _applicationInfosTask = _environment.GetApplicationInfos();
            _federation = _environment.GetFederation();
        }
        
        private async Task<Nest.IElasticClient> CreateESClient(string type, string param = "")
        {
            var result = await _cFactory.CreateClient(type, TABLE_NAME, param);
            return result;
        }

        private UriBuilder GetEndpoint()
        {
            var nodes = _cFactory.GetConnectionPool("analytics").Nodes.ToList();
            return new UriBuilder(nodes[0].Uri.AbsoluteUri ?? "http://localhost:9200/");
        }
        
        /// <summary>
        /// Compute a different index by accountID + appName + week + type.
        /// </summary>
        /// <param name="type">Type of index to create</param>
        /// <returns>Return the computed index name</returns>
        private string GetIndexName(string type)
        {
            return _cFactory.GetIndex(type, TABLE_NAME, GetWeek(DateTime.UtcNow).ToString());
        }

        /// <summary>
        /// Get all index between the start date and the end date.
        /// </summary>
        /// <param name="weeks">IEnumerable of start date and end date</param>
        /// <param name="types">Type(s) filter</param>
        /// <returns>Computed indexes</returns>
        private string CreateIndicesParameter(IEnumerable<int> weeks, string[] types)
        {
            string result = "";
            foreach (string type in types)
            {
                foreach (int week in weeks)
                {
                    result += _cFactory.GetIndex(type, TABLE_NAME, week);
                }
            }
            return result;
        }

        /// <summary>
        /// Get week number.
        /// </summary>
        /// <param name="date"></param>
        /// <returns>return weak number</returns>
        private long GetWeek(DateTime date)
        {
            return date.Ticks / (TimeSpan.TicksPerDay * 7);
        }

        public async Task Flush()
        {
           
            async Task FlushStore(string dataType, ConcurrentQueue<AnalyticsDocument> docs)
            {
                var client = await CreateESClient(dataType, GetWeek(DateTime.UtcNow).ToString());

                List<AnalyticsDocument> documentToBulk = new List<AnalyticsDocument>();

                while (docs.TryDequeue(out AnalyticsDocument doc))
                {
                    doc.Index = client.ConnectionSettings.DefaultIndex;
                    
                    var appInfos = await _applicationInfosTask;
                    var fed = await _federation;
                    doc.AccountId = appInfos.AccountId;
                    doc.App = appInfos.ApplicationName;
                    doc.Cluster = fed.current.id;
                    doc.DeploymentId = appInfos.DeploymentId;
                    documentToBulk.Add(doc);
                }

                if (documentToBulk.Count > 0) 
                {
                    var r = await client.BulkAsync(bd => bd.IndexMany<AnalyticsDocument>(documentToBulk));
                    if (!r.IsValid)
                    {
                        _logger.Log(LogLevel.Error, "analytics", "Failed to index analytics", r.OriginalException);
                    }
                }
            }

            var tasks = new List<Task>();
            foreach (var kvp in _documents)
            {
                var dataType = kvp.Key;
                var docs = kvp.Value;
                tasks.Add(FlushStore(dataType, docs));
                
            }

            await Task.WhenAll(tasks);

        }
        
        /// <summary>
        /// Push data in memory
        /// </summary>        
        /// <param name="content">String to store</param>
        public void Push(AnalyticsDocument content)
        {
            var store = _documents.GetOrAdd(content.Type, t => new ConcurrentQueue<AnalyticsDocument>());
            content.CreationDate = DateTime.UtcNow;
            store.Enqueue(content);
        }

        /// <summary>
        /// Push data in memory
        /// </summary>
        /// <param name="group">Index type where the data will be store</param>
        /// <param name="category">category of analytics document, for search purpose</param>
        /// <param name="document">Json object to store</param>
        public void Push(string group,string category, JObject content)
        {
            AnalyticsDocument document = new AnalyticsDocument { Content = content, Type = group, Category = category };
            Push(document);
        }
    }
}

