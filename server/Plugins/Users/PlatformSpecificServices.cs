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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    class PlatformSpecificServices : IPlatformSpecificServices
    {
        private IEnumerable<IPlatformSpecificServiceImpl> _platformServices;

        public PlatformSpecificServices(IEnumerable<IPlatformSpecificServiceImpl> platformServices)
        {
            _platformServices = platformServices;
        }

        private IPlatformSpecificServiceImpl GetServiceForPlatform(string platform)
        {
            var service = _platformServices.FirstOrDefault(s => s.Platform == platform);

            return service;
        }


        public async Task<string> GetDisplayableUserId(PlatformId platformId)
        {
            var service = GetServiceForPlatform(platformId.Platform);

            if (service != null)
            {
                return await service.GetDisplayableUserId(platformId);
            }
            else
            {
                return null;
            }
        }

        public async Task<Dictionary<PlatformId, string>> GetDisplayableUserIds(IEnumerable<PlatformId> platformIds)
        {
            var groups = platformIds.GroupBy(pId => pId.Platform);

            var results = await Task.WhenAll(groups.Select(group => GetServiceForPlatform(group.Key)?.GetDisplayableUserIds(group) ?? Task.FromResult(group.ToDictionary(v => v, v => default(string)))));

            return results.SelectMany(dict => dict).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
           
           
        }
    }
}
