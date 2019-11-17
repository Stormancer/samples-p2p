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
using Server.Plugins.API;
using Stormancer;
using Stormancer.Core;
using Stormancer.Plugins;
using Stormancer.Plugins.ServiceLocator;
using System.Reactive.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Server.Plugins.Notification
{
    class NotificationChannelController : ControllerBase
    {
        private readonly INotificationChannel channel;

        public NotificationChannelController(INotificationChannel channel)
        {
            this.channel = channel;
        }

        [Api(ApiAccess.Scene2Scene, ApiType.Rpc)]
        public Task<bool> SendNotification(string type, InAppNotification data)
        {            
            return channel.SendNotification(type, data);
        }
    }

    class ProxyNotificationChannel : INotificationChannel
    {
        private readonly ISceneHost _scene;
        private readonly ISerializer _serializer;

        public ProxyNotificationChannel(ISceneHost scene, ISerializer serializer)
        {
            _serializer = serializer;
            _scene = scene;
        }

        public async Task<bool> SendNotification(string type, dynamic data)
        {
            var notif = data as InAppNotification;

            if (notif == null)
            {
                return false;
            }

            using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
            {
                var locator = scope.Resolve<IServiceLocator>();

                var rpc = _scene.DependencyResolver.Resolve<RpcService>();
                var packet = await rpc.Rpc("NotificationChannel.SendNotification", new MatchSceneFilter(await locator.GetSceneId("stormancer.plugins.notifications", "")), s =>
                {
                    _serializer.Serialize(type, s);
                    _serializer.Serialize(notif, s);
                }, PacketPriority.MEDIUM_PRIORITY).LastOrDefaultAsync();

                if (packet == null)
                {
                    throw new InvalidOperationException("Failed to send notification. (no response)");
                }

                using (packet.Stream)
                {
                    return _serializer.Deserialize<bool>(packet.Stream);
                }
            }
        }
    }
}
