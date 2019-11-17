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

using Server.Plugins.API;
using Stormancer;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.Analytics
{
    class AnalyticsController : ControllerBase
    {
        private readonly IAnalyticsService _analytics;
        private readonly ILogger _logger;
        private const string TypeRegex = "^[A-Za-z0-9_-]+$";

        public AnalyticsController(IAnalyticsService analytics, ILogger logger)
        {
            _logger = logger;
            _analytics = analytics;
        }

        public Task Push(Packet<IScenePeerClient> ctx)
        {           
            var documentDto = ctx.ReadObject<DocumentDto>();
            if (documentDto.Type == null)
            {
                throw new ArgumentNullException($"invalid analytics document received. 'Type' is null");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(documentDto.Type, TypeRegex))
            {
                _logger.Log(LogLevel.Error, "analytics", $"Invalid analytics type type received : {documentDto.Type}", documentDto);
            }

            // Add some meta data for kibana
            try
            {
                Newtonsoft.Json.Linq.JObject content = Newtonsoft.Json.Linq.JObject.Parse(documentDto.Content);//Try parse document            
                AnalyticsDocument document = new AnalyticsDocument { Type = documentDto.Type, Content = content, CreationDate = DateTime.UtcNow };
                _analytics.Push(document);
            }
            catch (Exception)
            {
                _logger.Log(LogLevel.Error, "analytics", $"Invalid analytics json received", documentDto.Content);
            }
            return Task.CompletedTask;
        }
    }
}

