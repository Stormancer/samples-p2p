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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Users.Test
{
    class TestAuthenticationProvider : IAuthenticationProvider
    {
        private readonly IUserSessions _sessions;
        private readonly ISerializer _serializer;

        public TestAuthenticationProvider(IUserSessions sessions, ISerializer serializer)
        {
            _sessions = sessions;
            _serializer = serializer;
        }

        public string Type => "test";

        public void AddMetadata(Dictionary<string, string> result)
        {
            
        }

        public Task<AuthenticationResult> Authenticate(AuthenticationContext authenticationCtx, CancellationToken ct)
        {
            AuthenticationResult result;

            if (authenticationCtx.Parameters.TryGetValue("testkey", out var testValue) && testValue == "testvalue")
            {
                result = AuthenticationResult.CreateSuccess(new User { Id = Guid.NewGuid().ToString() }, PlatformId.Unknown, authenticationCtx.Parameters);

                if (authenticationCtx.Parameters.TryGetValue("testrenewal", out var testRenewal))
                {
                    result.ExpirationDate = DateTime.Now + TimeSpan.Parse(testRenewal);
                    using (var sessionData = new MemoryStream())
                    {
                        _serializer.Serialize("initial", sessionData);
                        result.initialSessionData["testData"] = sessionData.ToArray();
                    }
                }
            }
            else
            {
                result = AuthenticationResult.CreateFailure("Invalid auth parameters, should be testkey=testvalue", PlatformId.Unknown, authenticationCtx.Parameters);
            }

            return Task.FromResult(result);
        }

        public Task OnGetStatus(Dictionary<string, string> status, Session session)
        {
            return Task.CompletedTask;
        }

        public async Task<DateTime?> RenewCredentials(AuthenticationContext authenticationContext)
        {
            if (authenticationContext.Parameters.TryGetValue("testData", out var data))
            {
                await _sessions.UpdateSessionData(authenticationContext.CurrentSession.SessionId, "testData", data);
            }
            else
            {
                throw new Exception("Expected 'testData' entry in parameters");
            }
            return null;
        }

        public Task Setup(Dictionary<string, string> parameters)
        {
            return Task.CompletedTask;
        }

        public Task Unlink(User user)
        {
            return Task.CompletedTask;
        }
    }
}

