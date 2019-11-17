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

using MsgPack.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    public class AuthParameters
    {
        [MessagePackMember(0)]
        public string Type { get; set; }
        [MessagePackMember(1)]
        public Dictionary<string,string> Parameters { get; set; }
    }

    public class RenewCredentialsParameters
    {
        [MessagePackMember(0)]
        public Dictionary<string, string> Parameters { get; set; }
    }

    public class RememberDeviceParameters
    {
        [MessagePackMember(0)]
        public string UserId { get; set; }
        [MessagePackMember(1)]
        public string UserDeviceId { get; set; }
    }

    public interface IAuthenticationService
    {
        Task SetupAuth(AuthParameters auth);
        Task<LoginResult> Login(AuthParameters auth, IScenePeerClient peer, CancellationToken ct);
        Task RememberDeviceFor2fa(RememberDeviceParameters auth, IScenePeerClient peer, CancellationToken ct);
        Dictionary<string, string> GetMetadata();
        Task<Dictionary<string, string>> GetStatus(IScenePeerClient peer);
        Task Unlink(User user, string type);
        /// <summary>
        /// Renew the credentials of authenticated users, if needed.
        /// </summary>
        /// <param name="threshold">How much time in advance of expiration should credentials be renewed.</param>
        /// <returns>The closest new credential expiration date.</returns>
        Task<DateTime?> RenewCredentials(TimeSpan threshold);
    }
}

