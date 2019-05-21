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
using Stormancer.Server.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stormancer.Core;
using Stormancer.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace Stormancer.Server.Users
{
    public class DeviceIdentifierAuthenticationProvider : IAuthenticationProvider
    {
        public const string PROVIDER_NAME = "deviceidentifier";
        private const string ClaimPath = "deviceid";
        private readonly ILogger _logger;
        private readonly IUserService _users;

        public string Type => PROVIDER_NAME;

        public DeviceIdentifierAuthenticationProvider(IUserService users, ILogger logger)
        {
            _logger = logger;
            _users = users;
        }

        public void AddMetadata(Dictionary<string, string> result)
        {
            result.Add("provider.deviceidentifier", "enabled");
        }

        public async Task<AuthenticationResult> Authenticate(AuthenticationContext authenticationCtx, CancellationToken ct)
        {
            var pId = new PlatformId { Platform = PROVIDER_NAME };
            string identifier;
            if(!authenticationCtx.Parameters.TryGetValue("deviceidentifier", out identifier))
            {
                return AuthenticationResult.CreateFailure("Device identifier must not be empty.", pId, authenticationCtx.Parameters);
            }
            var user = await _users.GetUserByClaim(PROVIDER_NAME, ClaimPath, identifier);
            if(user == null)
            {
                var uid = Guid.NewGuid().ToString("N");
                user = await _users.CreateUser(uid, JObject.FromObject(new { deviceidentifier = identifier }));

                var claim = new JObject { { ClaimPath, identifier } };
                user = await _users.AddAuthentication(user, PROVIDER_NAME, claim, identifier);
            }

            pId.OnlineId = user.Id;

            return AuthenticationResult.CreateSuccess(user, pId, authenticationCtx.Parameters);
        }

        public Task Setup(Dictionary<string, string> parameters)
        {
            throw new NotImplementedException();
        }
    }
}
