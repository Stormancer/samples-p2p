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
using Nest;
using Stormancer.Server.Database;
using Stormancer.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Plugins.Notification
{
    public class InAppNotificationRepository
    {
        private readonly ILogger _logger;
        private const string INDEX_NAME = "inappnotification";
        private Task<IElasticClient> _client;

        public InAppNotificationRepository(ILogger logger, IESClientFactory clientFactory)
        {
            _logger = logger;
            _client = clientFactory.CreateClient<InAppNotificationRecord>(INDEX_NAME);
        }

        public async Task IndexNotification(InAppNotificationRecord notif)
        {
            var client = await _client;

            if (string.IsNullOrEmpty(notif.Id))
            {
                notif.Id = Guid.NewGuid().ToString("N");
            }
            
            await client.IndexDocumentAsync(notif);
        }
        
        public async Task<IEnumerable<InAppNotificationRecord>> GetPendingNotifications(string userId)
        {
            var client = await _client;

            var result = await client.SearchAsync<InAppNotificationRecord>(sd => sd
                .Size(1000)
                .Sort(ss => ss
                    .Ascending(p => p.CreatedOn)
                )
                .Query(query => query
                    .Bool(b => b
                        .Must(
                            q => q.Term(tq => tq
                                .Field("userId.keyword")
                                .Value(userId)
                            )
                        )
                    )
                )
            );

            return result.Documents;
        }

        public async Task DeleteNotifications(List<InAppNotificationRecord> expiredNotifs)
        {
            var client = await _client;
            await client.DeleteManyAsync(expiredNotifs);
        }

        public async Task AcknowledgeNotification(string notificationId)
        {
            var client = await _client;
            await client.DeleteAsync<InAppNotificationRecord>(notificationId);
        }
    }
}
