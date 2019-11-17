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
using Server.Plugins.Notification;
using Stormancer.Diagnostics;
using Stormancer.Server.Users;
using System;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace Server.Plugins.Notification
{
    public class InAppNotificationsAdminController : ApiController
    {
        private readonly InAppNotificationRepository _repository;
        private ILogger _logger;

        private readonly INotificationChannel _notificationChannel;
        private readonly IUserSessions _sessions;

        public InAppNotificationsAdminController(INotificationChannel notificationChannel, IUserSessions sessions, InAppNotificationRepository repository, ILogger logger)
        {
            _notificationChannel = notificationChannel;
            _sessions = sessions;
            _repository = repository;
            _logger = logger;
        }

        [ActionName("inappnotifications")]
        [HttpPost]
        public async Task Post([FromBody]dynamic data)
        {
            

            if (data == null || data.Acknowledgment == null || data.broadcastMode == null || data.Message == null || data.UserId == null || data.NotificationType == null)
            {
                _logger.Log(LogLevel.Warn, "InAppNotificationAdminController", "Notification is not valid", new { data });
                return;
            }

            var notif = new InAppNotification { Acknowledgment = data.Acknowledgment, NotificationType = data.NotificationType, Message = data.Message, UserId = data.UserId, CreatedOn = DateTime.UtcNow, Type = "notification.admin" };

            var broadcastMode = (BroadcastMode)(data.BroadcastMode ?? BroadcastMode.None);
            if (broadcastMode == BroadcastMode.AllConnectedUsers)
            {
                // TODO
                await _notificationChannel.SendNotification("AdminNotificationBroadcastAllConnectedUsers", notif);
            }
            else if (broadcastMode == BroadcastMode.AllUsers)
            {
                // TODO
                //await _notificationChannel.SendNotification("AdminNotificationBroadcastAllUsers", notif);
            }
            else //if (broadcastMode == BroadcastMode.None)
            {
                var record = new InAppNotificationRecord(notif);
                await _repository.IndexNotification(record);
                // TODO
                await _notificationChannel.SendNotification("AdminNotification.ByUserId", notif);
            }
        }
    }

    enum BroadcastMode
    {
        None = 0,
        AllConnectedUsers = 1,
        AllUsers = 2,
    };
}
