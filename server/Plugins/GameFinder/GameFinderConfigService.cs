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
using Stormancer.Core;
using Stormancer.Plugins;
using System.Collections.Generic;
using Server.Plugins.Notification;
using System.Threading.Tasks;
using Stormancer.Server.Users;
using Newtonsoft.Json.Linq;
using Server.Plugins.Configuration;
using System.Linq;

namespace Stormancer.Server.GameFinder
{

    //TODO: change to make it generic
    class GameFinderConfigService : IGameFinderConfigService
    {
        private Dictionary<string, JObject> _regionMappings;
        private readonly INotificationChannel _notificationChannel;

        public GameFinderConfigService(
            IConfiguration config,
            INotificationChannel notificationChannel)
        {
            _notificationChannel = notificationChannel;

            config.SettingsChanged += (s, c) => ApplyConfig(c);
            ApplyConfig(config.Settings);
        }

        private void ApplyConfig(dynamic config)
        {
            var RegionMappings = ((JObject)config.regionsMapping).ToObject<Dictionary<string, JObject>>() ?? new Dictionary<string, JObject>();

            bool isDirty = ShouldRefreshConfig(RegionMappings);

            if (isDirty)
            {
                _regionMappings = RegionMappings;
                var payload = JObject.FromObject(_regionMappings);

                //var notification = new InAppNotification { Acknowledgment = 0, NotificationType = InAppNotificationType.RegionConfig, Message = "Updating Config", Data = payload.ToString(), UserId = "*", CreatedOn = System.DateTime.UtcNow, Type = "notification.admin" };

                //_notificationChannel.SendNotification("AdminNotificationBroadcastAllConnectedUsers", notification);
            }
        }

        public Dictionary<string, JObject> GetRegions()
        {
            return _regionMappings;
        }

        public bool ShouldRefreshConfig(Dictionary<string, JObject> newRegionMappings)
        {
            if (newRegionMappings == null)
            {
                return false;
            }

            if (_regionMappings == null)
            {
                return true;
            }

            if (!Enumerable.SequenceEqual(newRegionMappings.Keys, _regionMappings.Keys))
            {
                return true;
            }
            else
            {
                foreach (string Key in newRegionMappings.Keys)
                {
                    JObject newRegionConfig = newRegionMappings[Key];
                    JObject oldRegionConfig = _regionMappings[Key];
                    if (!JToken.DeepEquals(newRegionConfig,oldRegionConfig))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}

