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
using Server.Plugins.Configuration;
using Stormancer.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    class AuthenticationService : IAuthenticationService
    {
        private readonly UserManagementConfig _config;
        private readonly ILogger _logger;

        private readonly IEnumerable<IAuthenticationProvider> _authProviders;
        private readonly IUserService _users;
        private readonly UserSessions _sessions;
        private readonly Func<IEnumerable<IAuthenticationEventHandler>> _handlers;

        public AuthenticationService(
            Func<IEnumerable<IAuthenticationEventHandler>> handlers,
            IEnumerable<IAuthenticationProvider> providers,
            UserManagementConfig config,
            IUserService users,
            UserSessions sessions,
            ILogger logger
            )
        {
            _config = config;
            _logger = logger;
            _authProviders = providers;
            _users = users;
            _sessions = sessions;
            _handlers = handlers;

        }

        private void ApplyConfig(IConfiguration config)
        {

        }
        private IEnumerable<IAuthenticationProvider> GetProviders()
        {
            return _authProviders;//.Where(p => _config.EnabledAuthenticationProviders.Contains(p.GetType()));
        }
        public Dictionary<string, string> GetMetadata()
        {
            var metadata = new Dictionary<string, string>();
            foreach (var provider in GetProviders())
            {
                provider.AddMetadata(metadata);
            }

            return metadata;
        }

        public async Task<LoginResult> Login(AuthParameters auth, IScenePeerClient peer, CancellationToken ct)
        {

            var result = new LoginResult();

            var authenticationCtx = new AuthenticationContext(auth.Parameters, peer);
            var validationCtx = new ValidateAuthenticationAttemptContext { AuthCtx = authenticationCtx, Type =auth.Type };
            await _handlers().RunEventHandler(h => h.Validate(validationCtx), ex => _logger.Log(LogLevel.Error, "user.login", "An error occured while running Validate event handler", ex));

            
            //if(validationCtx.HasError)
            //{
            //    throw new ClientException(validationCtx.Reason);
            //    //result.Success = false;  // currently doesnt return error to client with verison mismatch reason
            //    //result.ErrorMsg = validationCtx.Reason;
            //    //return result;
            //}
            
    
            AuthenticationResult authResult = null;
            var provider = GetProviders().FirstOrDefault(p => p.Type == auth.Type);
            if (provider == null)
            {
                throw new ClientException($"authentication.notSupported?type={auth.Type}");
            }

            authResult = await provider.Authenticate(authenticationCtx, ct);

           
            if (authResult.Success)
            {
                _logger.Log(LogLevel.Trace, "user.login", "Authentication successful.", authResult);
                var oldPeer = await _sessions.GetPeer(authResult.AuthenticatedUser.Id);
                if (oldPeer != null && oldPeer.SessionId != peer.SessionId)
                {
                    try
                    {
                        await oldPeer.DisconnectFromServer("auth.login.new_connection");
                    }
                    catch (Exception)
                    {

                    }

                    await _sessions.Login(peer, authResult.AuthenticatedUser, authResult.PlatformId, authResult.initialSessionData);
                }
                if (oldPeer == null)
                {
                    await _sessions.Login(peer, authResult.AuthenticatedUser, authResult.PlatformId, authResult.initialSessionData);
                }
                result.Success = true;
                result.UserId = authResult.AuthenticatedUser.Id;
                result.Username = authResult.Username;

            }
            else
            {
                _logger.Log(LogLevel.Warn, "user.login", "Authentication failed.", authResult);

                result.ErrorMsg = authResult.ReasonMsg;

            }


            if (!result.Success)
            {
                // FIXME: Temporary workaround to issue where disconnections cause large increases in CPU/Memory usage
                //var _ = Task.Delay(1000).ContinueWith(t => peer.DisconnectFromServer("auth.login.failed"));
            }

            return result;

        }

        public async Task SetupAuth(AuthParameters auth)
        {
            var provider = GetProviders().FirstOrDefault(p => p.Type == auth.Type);
            if (provider == null)
            {
                throw new ClientException($"authentication.notSupported?type={auth.Type}");
            }

            await provider.Setup(auth.Parameters);

        }

        
    }
}
