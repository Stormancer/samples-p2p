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
using Stormancer.Server.Database;
using Server.Plugins.Configuration;
using Server.Plugins.Notification;
using Stormancer;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Stormancer.Plugins;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Nest;
using Stormancer.Server.Users;

namespace Server.Plugins.Notification
{
    class InAppNotificationProvider : INotificationProvider
    {
        public InAppNotificationProvider(ISceneHost scene, ISerializer serializer, ILogger logger, IConfiguration configuration, IUserSessions userSessions, InAppNotificationRepository repository)
        {
            _scene = scene;
            this.serializer = serializer;
            _logger = logger;
            _repository = repository;

            configuration.SettingsChanged += (_, settings) => ApplyConfig(settings);
            ApplyConfig(configuration.Settings);

            _userSessions = userSessions;


            _scene.Connected.Add(OnConnected);

            _scene.AddProcedure("inappnotification.acknowledgenotification", async (RequestContext<IScenePeerClient> ctx) =>
            {
                var notificationId = ctx.ReadObject<string>();

                if (string.IsNullOrEmpty(notificationId))
                {
                    throw new ClientException("Bad notificationId.");
                }

                await AcknowledgeNotification(notificationId);
            });
        }

        public async Task AcknowledgeNotification(string notificationId)
        {
            await _repository.AcknowledgeNotification(notificationId);
        }

        private void ApplyConfig(dynamic settings)
        {
        }

        public Task OnConnected(IScenePeerClient client)
        {
            //var user = await _userSessions.GetUser(client);
            //var notifs = await _repository.GetPendingNotifications(user.Id);

            //var defaultDate = new DateTime();

            //var expiredNotifs = notifs.Where(n => n.ExpirationDate != defaultDate && n.ExpirationDate < DateTime.UtcNow).ToList();

            //if (expiredNotifs.Count > 0)
            //{
            //    await _repository.DeleteNotifications(expiredNotifs);
            //    notifs = notifs.Except(expiredNotifs);
            //}

            //if (notifs.Count() > 0)
            //{
            //    foreach (var notification in notifs)
            //    {
            //        client.Send("inappnotification.push", new InAppNotification(notification));
            //        if (notification.Acknowledgment == InAppNotificationAcknowledgment.OnSend)
            //        {
            //            var _ = AcknowledgeNotification(notification.Id); // don't await the notifications
            //        }
            //    }
            //}
            return Task.CompletedTask;
        }

        public async Task<bool> SendNotification(string type, dynamic data)
        {
            var notif = data as InAppNotification;

            if (notif == null)
            {
                return false;
            }

            PeerFilter filter;
            bool sent = false;

            if (string.IsNullOrWhiteSpace(notif.UserId))
            {
                _logger.Log(Stormancer.Diagnostics.LogLevel.Warn, "InAppNotificationProvider", "UserId is null", data);
                return false;
            }
            if (notif.UserId == "*")
            {
                filter = new MatchArrayFilter(_scene.RemotePeers);
                // TODO fix matchAll
                //filter = new MatchAllFilter();
            }
            else
            {
                var list = new List<string>();
                foreach (var userId in notif.UserId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var session = await _userSessions.GetSessionByUserId(userId);
                    if (session != null)
                    {
                        list.Add(session.SessionId);
                        sent = true;
                    }
                }
                filter = new MatchArrayFilter(list);
            }

            notif.Id = Guid.NewGuid().ToString("N");
            notif.UserId = "";
            notif.CreatedOn = DateTime.UtcNow;

            await _scene.Send(filter, "inappnotification.push", s => serializer.Serialize(notif, s), PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_ORDERED);

            if (notif.Acknowledgment == InAppNotificationAcknowledgment.OnReceive || notif.Acknowledgment == InAppNotificationAcknowledgment.ByUser || (!sent && notif.Acknowledgment == InAppNotificationAcknowledgment.OnSend))
            {
                var record = new InAppNotificationRecord(notif);

                await _repository.IndexNotification(record);
            }

            return true;
        }

        private readonly ISceneHost _scene;
        private readonly ISerializer serializer;
        private readonly ILogger _logger;
        private readonly InAppNotificationRepository _repository;
        private readonly IUserSessions _userSessions;
    }
}
