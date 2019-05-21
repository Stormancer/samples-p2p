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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;
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
        private string _deploymentId;

        private readonly Stormancer.Diagnostics.ILogger _logger;
        private string _nlogConfigFile;
        private bool _nlogLoggingEnabled = false;
        private int? _elasticsearchAggregationTime;
        private Task _flushingTask;
        private readonly NLog.ILogger _nLogger = LogManager.GetLogger("analytics");

        public AnalyticsService(
            IESClientFactory clientFactory,
            IEnvironment environment,
            Stormancer.Diagnostics.ILogger logger,
            IConfiguration configuration
        )
        {
            _cFactory = clientFactory;
            _environment = environment;
            _logger = logger;
            _applicationInfosTask = _environment.GetApplicationInfos();
            configuration.SettingsChanged += (s, c) => ApplyConfig(c);
            ApplyConfig(configuration.Settings);

            ConfigurationItemFactory.Default.JsonConverter = new JsonNetSerializer();
        }

        private void ApplyConfig(dynamic config)
        {
            var previousNlogConfigFileValue = _nlogConfigFile;
            _nlogConfigFile = (string)config?.analytics?.NLog?.config;


            _elasticsearchAggregationTime = (int?)config?.analytics?.elasticsearch?.aggregationTime;

            if (_nlogConfigFile != null)
            {
                if (_nlogConfigFile != previousNlogConfigFileValue)
                {
                    lock (this)
                    {
                        try
                        {
                            LogManager.Configuration = new XmlLoggingConfiguration(_nlogConfigFile);
                            _nlogLoggingEnabled = true;
                        }
                        catch (Exception ex)
                        {
                            _nlogLoggingEnabled = false;
                            _logger.Log(Stormancer.Diagnostics.LogLevel.Error, "Analytics.Nlog", $"Unable to load Nlog configuration file at '{_nlogConfigFile}'", ex);
                        }
                    }
                }
            }
            else
            {
                _nlogLoggingEnabled = false;
            }

            if (_elasticsearchAggregationTime.HasValue && _flushingTask?.Status != TaskStatus.Running)
            {
                lock (this)
                {
                    if (_elasticsearchAggregationTime.HasValue && _flushingTask?.Status != TaskStatus.Running)
                    {
                        _flushingTask = FlushPeriodically();
                    }
                }
            }
        }

        private async Task FlushPeriodically()
        {
            var aggregationTime = _elasticsearchAggregationTime;
            while (aggregationTime.HasValue)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(aggregationTime.GetValueOrDefault()));
                    await ElasticSearchFlush();
                }
                catch (Exception ex)
                {
                    _logger.Log(Stormancer.Diagnostics.LogLevel.Error, "analytics", "Failed to flush analytics.", ex);
                }
                aggregationTime = _elasticsearchAggregationTime;
            }
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

        private async Task ElasticSearchFlush()
        {
            foreach (var store in _documents)
            {
                var dataType = store.Key;
                var client = await CreateESClient(dataType, GetWeek(DateTime.UtcNow).ToString());

                List<AnalyticsDocument> documentToBulk = new List<AnalyticsDocument>();

                while (store.Value.TryDequeue(out AnalyticsDocument doc))
                {
                    doc.Index = client.ConnectionSettings.DefaultIndex;
                    documentToBulk.Add(doc);
                }

                if (documentToBulk.Count > 0)
                {
                    var r = await client.BulkAsync(bd => bd.IndexMany<AnalyticsDocument>(documentToBulk));
                    if (!r.IsValid)
                    {
                        _logger.Log(Stormancer.Diagnostics.LogLevel.Error, "analytics", "Failed to index analytics", r.OriginalException);
                    }
                }
            }
        }

        /// <summary>
        /// Push data in memory
        /// </summary>        
        /// <param name="content">String to store</param>
        public void Push(AnalyticsDocument content)
        {
            _ = PushImpl(content);
        }

        public async Task PushImpl(AnalyticsDocument content)
        {
            try
            {
                content.CreationDate = DateTime.UtcNow;
                if (_deploymentId == null)
                {
                    var appInfos = await _applicationInfosTask;
                    _deploymentId = appInfos.DeploymentId;
                }
                content.DeploymentId = _deploymentId;

                if (_elasticsearchAggregationTime.HasValue)
                {
                    var store = _documents.GetOrAdd(content.Type, t => new ConcurrentQueue<AnalyticsDocument>());
                    store.Enqueue(content);
                }

                if (_nlogLoggingEnabled)
                {
                    _nLogger.Log(NLog.LogLevel.Info, "{content}", content);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(Stormancer.Diagnostics.LogLevel.Error, "Analytics.Push", $"An error occurred when trying to push analytics.", ex);
            }
        }

        /// <summary>
        /// Push data in memory
        /// </summary>
        /// <param name="type">Index type where the data will be store</param>
        /// <param name="document">Json object to store</param>
        public void Push(string type, JObject content)
        {
            AnalyticsDocument document = new AnalyticsDocument { Content = content, Type = type };
            Push(document);
        }
    }

    internal class JsonNetSerializer : IJsonConverter
    {
        readonly JsonSerializerSettings _settings;

        public JsonNetSerializer()
        {
            _settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };
        }

        /// <summary>Serialization of an object into JSON format.</summary>
        /// <param name="value">The object to serialize to JSON.</param>
        /// <param name="builder">Output destination.</param>
        /// <returns>Serialize succeeded (true/false)</returns>
        public bool SerializeObject(object value, StringBuilder builder)
        {
            try
            {
                var jsonSerializer = JsonSerializer.CreateDefault(_settings);
                var sw = new System.IO.StringWriter(builder, System.Globalization.CultureInfo.InvariantCulture);
                using (var jsonWriter = new JsonTextWriter(sw))
                {
                    jsonWriter.Formatting = Formatting.None;
                    jsonSerializer.Formatting = Formatting.None;
                    jsonSerializer.Serialize(jsonWriter, value, null);
                }
            }
            catch (Exception e)
            {
                NLog.Common.InternalLogger.Error(e, "Error when custom JSON serialization");
                return false;
            }
            return true;
        }
    }
}
