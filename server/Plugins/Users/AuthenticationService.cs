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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Server.Plugins.Configuration;
using Stormancer.Core;
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
        private readonly IUserSessions _sessions;
        private readonly Func<IEnumerable<IAuthenticationEventHandler>> _handlers;
        private readonly ISceneHost _scene;

        // Client RPC
        private const string RenewCredentialsRoute = "users.renewCredentials";

        public AuthenticationService(
            Func<IEnumerable<IAuthenticationEventHandler>> handlers,
            IEnumerable<IAuthenticationProvider> providers,
            UserManagementConfig config,
            IUserService users,
            IUserSessions sessions,
            ILogger logger,
            ISceneHost scene
            )
        {
            _config = config;
            _logger = logger;
            _authProviders = providers;
            _users = users;
            _sessions = sessions;
            _handlers = handlers;
            _scene = scene;
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
            var sessions = _sessions as UserSessions;
            if (sessions == null)
            {
                throw new InvalidOperationException("Cannot call login on another scene than the authenticator scene");
            }
            _logger.Log(LogLevel.Debug, "user.login", "AuthenticationService login", auth);

            var result = new LoginResult();
            var session = await sessions.GetSessionRecordById(peer.SessionId);
            var authenticationCtx = new AuthenticationContext(auth.Parameters, peer, session?.CreateView());
            var validationCtx = new LoggingInCtx { AuthCtx = authenticationCtx, Type = auth.Type };
            await _handlers().RunEventHandler(h => h.OnLoggingIn(validationCtx), ex => _logger.Log(LogLevel.Error, "user.login", "An error occured while running Validate event handler", ex));


            if (!validationCtx.HasError)
            {
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
                    if (authResult.AuthenticatedUser != null)
                    {
                        var oldPeer = await _sessions.GetPeer(authResult.AuthenticatedUser.Id);
                        if (oldPeer != null && oldPeer.SessionId != peer.SessionId)
                        {
                            try
                            {
                                await sessions.LogOut(oldPeer);
                                await oldPeer.DisconnectFromServer("auth.login.new_connection");
                            }
                            catch (Exception)
                            {

                            }

                            await sessions.Login(peer, authResult.AuthenticatedUser, authResult.PlatformId, authResult.initialSessionData);
                        }
                        if (oldPeer == null)
                        {
                            await sessions.Login(peer, authResult.AuthenticatedUser, authResult.PlatformId, authResult.initialSessionData);
                        }
                    }

                    await sessions.Login(peer, authResult.AuthenticatedUser, authResult.PlatformId, authResult.initialSessionData);



                    await sessions.UpdateSession(peer.SessionId, s =>
                    {
                        s.Authentications[provider.Type] = authResult.PlatformId.ToString();
                        if (authResult.ExpirationDate.HasValue)
                        {
                            s.AuthenticationExpirationDates[provider.Type] = authResult.ExpirationDate.Value;
                        }

                        authResult.OnSessionUpdated?.Invoke(s);
                        return Task.FromResult(s);
                    });
                    result.Success = true;
                    result.UserId = authResult?.AuthenticatedUser?.Id;
                    result.Username = authResult.Username;
                    session = await sessions.GetSessionRecordById(peer.SessionId);
                    result.Authentications = session.Authentications.ToDictionary(entry => entry.Key, entry => entry.Value);
                    var ctx = new LoggedInCtx { Result = result, Session = session };
                    await _handlers().RunEventHandler(h => h.OnLoggedIn(ctx), ex => _logger.Log(LogLevel.Error, "user.login", "An error occured while running OnLoggedIn event handler", ex));


                }
                else
                {
                    //_logger.Log(LogLevel.Warn, "user.login", $"Authentication failed, reason: {authResult.ReasonMsg}", authResult);

                    result.ErrorMsg = authResult.ReasonMsg;

                }
            }
            else
            {
                //throw new ClientException(validationCtx.Reason);
                result.Success = false;  // currently doesnt return error to client with verison mismatch reason
                result.ErrorMsg = validationCtx.Reason;
                //return result;
            }

            if (!result.Success && session == null)
            {
                // FIXME: Temporary workaround to issue where disconnections cause large increases in CPU/Memory usage
                var _ = Task.Delay(1000).ContinueWith(t => peer.DisconnectFromServer(result.ErrorMsg));
            }

            return result;

        }

        public async Task RememberDeviceFor2fa(RememberDeviceParameters p, IScenePeerClient peer, CancellationToken ct)
        {

            if (string.IsNullOrWhiteSpace(p.UserId))
            {
                throw new ClientException($"authentication.RememberDeviceFor2fa.missingUserId");
            }
            if (string.IsNullOrWhiteSpace(p.UserDeviceId))
            {
                throw new ClientException($"authentication.RememberDeviceFor2fa.UserDeviceId");
            }

            var user = await _users.GetUser(p.UserId);


            Dictionary<string, string> RememberedDeviceIds = new Dictionary<string, string>();
            //Dictionary<string, string> RememberedDeviceIds = JsonConvert.DeserializeObject<Dictionary<string, string>>(user.UserData["RememberedDevices"].ToString());
            if (user.UserData["RememberedDevices"] != null)
            {
                RememberedDeviceIds = JsonConvert.DeserializeObject<Dictionary<string, string>>(user.UserData["RememberedDevices"].ToString());
            }

            // remove expired devices from dictionary
            foreach (KeyValuePair<string, string> entry in RememberedDeviceIds)
            {
                DateTime RememberedDate = DateTime.ParseExact(entry.Value, "MM-dd-yyyy", System.Globalization.CultureInfo.InvariantCulture);
                if (DateTime.UtcNow - RememberedDate > TimeSpan.FromDays(30))
                {
                    RememberedDeviceIds.Remove(entry.Key);
                }
            }

            if (RememberedDeviceIds.ContainsKey(p.UserDeviceId))
            {
                throw new ClientException($"authentication.RememberDeviceFor2fa device already remembered. how was this device not recognized? this should not happen");
            }
            RememberedDeviceIds.Add(p.UserDeviceId, DateTime.UtcNow.ToString("MM-dd-yyyy"));
            user.UserData["RememberedDevices"] = JsonConvert.SerializeObject(RememberedDeviceIds);

            await _users.UpdateUserData(user.Id, user.UserData);
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

        public async Task<Dictionary<string, string>> GetStatus(IScenePeerClient peer)
        {
            var session = await _sessions.GetSession(peer);
            var result = new Dictionary<string, string>(session.Authentications.ToDictionary(entry=>entry.Key,entry=>entry.Value));
            var tasks = new List<Task>();
            foreach (var provider in _authProviders)
            {
                tasks.Add(provider.OnGetStatus(result, session));
            }
            await Task.WhenAll(tasks);
            return result;
        }

        public async Task Unlink(User user, string type)
        {
            var provider = GetProviders().FirstOrDefault(p => p.Type == type);
            if (provider == null)
            {
                throw new ClientException($"authentication.notSupported?type={type}");
            }


            await provider.Unlink(user);


        }

        public async Task<DateTime?> RenewCredentials(TimeSpan threshold)
        {
            var tasks = _scene.RemotePeers.Select(peer => RenewCredentials(threshold, peer));
            var whenAllTask = Task.WhenAll(tasks);
            try
            {
                await whenAllTask;
            }
            catch (Exception)
            {
                // I use whenAllTask.Exception instead of the caught Exception because I want the AggregateException
                _logger.Log(LogLevel.Warn, "AuthenticationService.RenewCredentials", "One or more credential renewals failed", whenAllTask.Exception);
            }

            return tasks.Select(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    return task.Result;
                }
                return null;
            })
            .Aggregate<DateTime?, DateTime?>(null, (closest, next) =>
            {
                if ((!closest.HasValue) || (next.HasValue && next.Value < closest.Value))
                {
                    return next;
                }
                return closest;
            });
        }

        private async Task<DateTime?> RenewCredentials(TimeSpan threshold, IScenePeerClient peer)
        {
            var sessions = _sessions as UserSessions;
            var session = await sessions.GetSessionRecordById(peer.SessionId);
            if (session == null)
            {
                return null;
            }
            DateTime? closestExpirationDate = null;
            // Copy the dictionary because I need to modify the values while iterating
            var expirationDatesCopy = new Dictionary<string, DateTime>(session.AuthenticationExpirationDates);
            foreach (var kvp in expirationDatesCopy)
            {
                var provider = _authProviders.FirstOrDefault(p => p.Type == kvp.Key);
                if (provider == null)
                {
                    // This is an internal error, it should definitely not happen
                    _logger.Log(LogLevel.Error, "AuthenticationService.RenewCredentials", "An AuthenticationProvider in a user session was not found in the list of providers. This is an SA bug.",
                        new
                        {
                            Provider = kvp.Key,
                            SessionId = session.SessionId,
                            UserId = session.User?.Id
                        }, session.SessionId, session.User?.Id ?? "", kvp.Key);
                    await peer.DisconnectFromServer("ServerError");
                    return closestExpirationDate;
                }

                if (kvp.Value <= DateTime.Now + threshold)
                {
                    try
                    {
                        var parameters = await peer.RpcTask<string, RenewCredentialsParameters>(RenewCredentialsRoute, provider.Type);
                        var ctx = new AuthenticationContext(parameters.Parameters, peer, session.CreateView());
                        var expiration = await provider.RenewCredentials(ctx);
                        if (expiration.HasValue)
                        {
                            session.AuthenticationExpirationDates[provider.Type] = expiration.Value;
                            if (!closestExpirationDate.HasValue || expiration.Value < closestExpirationDate.Value)
                            {
                                closestExpirationDate = expiration.Value;
                            }
                        }
                        else
                        {
                            session.AuthenticationExpirationDates.Remove(provider.Type);
                        }
                    }
                    catch (OperationCanceledException ex) when (ex.Message == "Peer disconnected")
                    {
                        return null;
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Error, "AuthenticationService.RenewCredentials", "An error occurred while renewing a user's credentials",
                            new
                            {
                                Exception = ex.ToString(),
                                Provider = provider.Type,
                                SessionId = session.SessionId,
                                UserId = session.User?.Id
                            }, provider.Type, session.SessionId, session.User?.Id ?? "");

                        await peer.DisconnectFromServer($"authentication.renewCredentialsError?provider={provider.Type}");
                        return null;
                    }
                }
            }

            return closestExpirationDate;
        }
    }
}

