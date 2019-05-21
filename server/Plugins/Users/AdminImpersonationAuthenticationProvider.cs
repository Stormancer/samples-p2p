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
using Stormancer.Core;
using Stormancer.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    class AdminImpersonationAuthenticationProvider : IAuthenticationProvider
    {
        private const string Provider_Name = "impersonation";
        private string _secret;
        private bool _isEnabled;
        private readonly ILogger logger;
        private readonly IUserService _users;

        public string Type => Provider_Name;
        public AdminImpersonationAuthenticationProvider(IConfiguration config, ILogger logger, IUserService users)
        {
            this.logger = logger;
            _users = users; 
            config.SettingsChanged += (s, e) => ApplyConfiguration(config);
            ApplyConfiguration(config);
        }

        public void AddMetadata(Dictionary<string, string> result)
        {
            //Don't add metadata.
        }

      


        private void ApplyConfiguration(IConfiguration configuration)
        {
            var auth = configuration.Settings.auth;
            if (auth != null)
            {
                var impersonation = auth.adminImpersonation;
                if (impersonation != null)
                {
                    if (impersonation.enabled != null)
                    {
                        try
                        {
                            _isEnabled = (bool)impersonation.enabled;
                        }
                        catch
                        {
                            _isEnabled = false;
                            logger.Error("users.adminImpersonation", "Failed to load auth.adminImpersonation.enabled (bool) config parameter. Impersonation disabled.");
                        }
                    }
                    if (impersonation.secret != null)
                    {
                        try
                        {
                            _secret = (string)impersonation.secret;
                        }
                        catch
                        {
                            _isEnabled = false;
                            logger.Error("users.adminImpersonation", "Failed to load auth.adminImpersonation.secret (string) config parameter. Impersonation disabled.");
                        }
                    }
                }
            }
        }

        public async Task<AuthenticationResult> Authenticate(AuthenticationContext authenticationCtx, CancellationToken ct )
        {
            var pId = new PlatformId { Platform = Provider_Name };
            if (!authenticationCtx.Parameters.TryGetValue("secret", out string secret) || string.IsNullOrWhiteSpace(secret))
            {
                return AuthenticationResult.CreateFailure("Missing impersonation secret.", pId, authenticationCtx.Parameters);
            }

            if (secret != _secret)
            {
                return AuthenticationResult.CreateFailure("Invalid impersonation secret.", pId, authenticationCtx.Parameters);
            }
            if (!authenticationCtx.Parameters.TryGetValue("impersonated-provider", out string ImpersonatingProvider) || string.IsNullOrWhiteSpace(ImpersonatingProvider))
            {
                return AuthenticationResult.CreateFailure("'impersonated-provider' must not be empty.", pId, authenticationCtx.Parameters);
            }

            if (!authenticationCtx.Parameters.TryGetValue("claimPath", out string ImpersonatingClaimPath) || string.IsNullOrWhiteSpace(ImpersonatingClaimPath))
            {
                return AuthenticationResult.CreateFailure("'claimPath' must not be empty.", pId, authenticationCtx.Parameters);
            }
            if (!authenticationCtx.Parameters.TryGetValue("claimValue", out string ImpersonatingClaimValue) || string.IsNullOrWhiteSpace(ImpersonatingClaimValue))
            {
                return AuthenticationResult.CreateFailure("'claimValue' must not be empty.", pId, authenticationCtx.Parameters);
            }
            var user = await _users.GetUserByClaim(ImpersonatingProvider, ImpersonatingClaimPath, ImpersonatingClaimValue);

            if (user == null)
            {
                return AuthenticationResult.CreateFailure($"The user '{ImpersonatingProvider}/{ImpersonatingClaimPath} = {ImpersonatingClaimValue}' does not exist.", pId, authenticationCtx.Parameters);
            }
            else
            {
                return AuthenticationResult.CreateSuccess(user, new PlatformId { Platform = ImpersonatingProvider, OnlineId = ImpersonatingClaimValue }, authenticationCtx.Parameters);
            }
        }

        public Task Setup(Dictionary<string, string> parameters)
        {
            throw new System.NotImplementedException();
        }
    }
}
