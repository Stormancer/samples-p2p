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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stormancer.Diagnostics;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Stormancer.Server.Users
{
    internal class CredentialsRenewer : IDisposable, IAuthenticationEventHandler
    {
        private readonly ILogger _logger;
        private readonly ISceneHost _scene;

        private static readonly TimeSpan DefaultCheckPeriod = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan DefaultRenewalThreshold = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MinimumCheckPeriodForAutoAdjust = TimeSpan.FromSeconds(1);

        // I use a long instead of a TimeSpan because I want to be able to make atomic read/writes
        private long _checkPeriod = DefaultCheckPeriod.Ticks;
        private long _renewalThreshold = DefaultRenewalThreshold.Ticks;

        /// <summary>
        /// The period at which users' credentials will be checked for renewal.
        /// </summary>
        /// <remarks>
        /// Lowering this value will immediately re-schedule the next renewal.
        /// </remarks>
        public TimeSpan CheckPeriod
        {
            get => TimeSpan.FromTicks(Interlocked.Read(ref _checkPeriod));
            private set
            {
                var previousValue = Interlocked.Exchange(ref _checkPeriod, value.Ticks);
                if (value.Ticks < previousValue)
                {
                    lock (_ctsLock)
                    {
                        // I do things in this order to avoid getting stuck in an infinite loop upon Task.Delay resuming
                        var currentCts = _periodShortenedCts;
                        _periodShortenedCts = new CancellationTokenSource();
                        currentCts.Cancel();
                        currentCts.Dispose();
                    }
                }
            }
        }
        /// <summary>
        /// How much time in advance of credentials expiration should they be renewed.
        /// </summary>
        /// <example>
        /// If it is 10am, and credentials for user A expire at 10.05am, they will be renewed immediately if <c>_renewalThreshold &gt;= 5</c>.
        /// </example>
        public TimeSpan RenewalThreshold
        {
            get => TimeSpan.FromTicks(Interlocked.Read(ref _renewalThreshold));
            private set => Interlocked.Exchange(ref _renewalThreshold, value.Ticks);
        }

        private object _ctsLock = new object();
        private CancellationTokenSource _periodShortenedCts = new CancellationTokenSource();

        public CredentialsRenewer(IConfiguration configuration, ILogger logger, ISceneHost scene)
        {
            _logger = logger;
            _scene = scene;

            configuration.SettingsChanged += (_, settings) => UpdateSettings(settings);
            UpdateSettings(configuration.Settings);
        }

        private void UpdateSettings(dynamic settings)
        {
            TimeSpan checkPeriodTemp = DefaultCheckPeriod;
            JToken checkPeriodEntry = (JToken)settings?.users?.credentialsRenewal?.checkPeriod;
            if (checkPeriodEntry != null)
            {
                if (checkPeriodEntry.Type != JTokenType.String || !TimeSpan.TryParse((string)checkPeriodEntry, out checkPeriodTemp))
                {
                    _logger.Log(LogLevel.Warn, "CredentialsRenewer.UpdateSettings", "Failed to parse value users.credentialsRenewal.checkPeriod into a TimeSpan. See https://docs.microsoft.com/fr-fr/dotnet/api/system.timespan.parse for formatting examples.", new { value = (string)checkPeriodEntry });
                    checkPeriodTemp = DefaultCheckPeriod;
                }
                else if (checkPeriodTemp < TimeSpan.Zero)
                {
                    _logger.Log(LogLevel.Warn, "CredentialsRenewer.UpdateSettings", "Value users.credentialsRenewal.checkPeriod cannot be negative.", new { value = (string)checkPeriodEntry });
                    checkPeriodTemp = DefaultCheckPeriod;
                }
            }

            TimeSpan thresholdTemp = DefaultRenewalThreshold;
            JToken renewalThresholdEntry = (JToken)settings?.users?.credentialsRenewal?.threshold;
            if (renewalThresholdEntry != null)
            {
                if (renewalThresholdEntry.Type != JTokenType.String || !TimeSpan.TryParse((string)renewalThresholdEntry, out thresholdTemp))
                {
                    _logger.Log(LogLevel.Warn, "CredentialsRenewer.UpdateSettings", "Failed to parse value users.credentialsRenewal.threshold into a TimeSpan. See https://docs.microsoft.com/fr-fr/dotnet/api/system.timespan.parse for formatting examples.", new { value = (string)renewalThresholdEntry });
                    thresholdTemp = DefaultRenewalThreshold;
                }
                else if (thresholdTemp < TimeSpan.Zero)
                {
                    _logger.Log(LogLevel.Warn, "CredentialsRenewer.UpdateSettings", "Value users.credentialsRenewal.threshold cannot be negative.", new { value = (string)renewalThresholdEntry });
                    thresholdTemp = DefaultRenewalThreshold;
                }
            }

            CheckPeriod = checkPeriodTemp;
            RenewalThreshold = thresholdTemp;
            _logger.Log(LogLevel.Info, "CredentialsRenewer.UpdateSettings", "Credentials renewal settings updated",
                new
                {
                    checkPeriod = new { key = "users.credentialsRenewal.checkPeriod", value = $"{CheckPeriod.TotalSeconds} seconds" },
                    threshold = new { key = "users.credentialsRenewal.threshold", value = $"{RenewalThreshold.TotalSeconds} seconds" }
                });
        }

        public async Task PeriodicRenewal(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    CancellationToken periodCt;
                    lock(_ctsLock)
                    {
                        periodCt = _periodShortenedCts.Token;
                    }
                    using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token, periodCt))
                    {
                        await Task.Delay(CheckPeriod, delayCts.Token);
                    }

                    _logger.Log(LogLevel.Trace, "CredentialsRenewer.PeriodicRenewal", "Checking connected users for credentials expiration...", new { });
                    DateTime? closestExpirationDate;
                    using (var scope = _scene.DependencyResolver.CreateChild(global::Server.Plugins.API.Constants.ApiRequestTag))
                    {
                        closestExpirationDate = await scope.Resolve<IAuthenticationService>().RenewCredentials(RenewalThreshold);
                    }

                    if (closestExpirationDate.HasValue && AdjustCheckPeriod(closestExpirationDate.Value, out var previousCheckPeriod))
                    {
                        _logger.Log(LogLevel.Warn, "CredentialsRenewer.PeriodicRenewal", "One or more renewed credentials have a shorter validity time than the current check period ; overriding the check period",
                            new
                            {
                                PreviousRenewalPeriod = $"{previousCheckPeriod.TotalSeconds} seconds",
                                NewRenewalPeriod = $"{CheckPeriod.TotalSeconds} seconds",
                            });
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, "CredentialsRenewer.PeriodicRenewal", "An error occurred during credentials renewal", ex);
                }
            }
        }

        private bool AdjustCheckPeriod(DateTime expirationDate, out TimeSpan previousCheckPeriod)
        {
            previousCheckPeriod = CheckPeriod;
            var validityTime = expirationDate - DateTime.Now;
            if (validityTime <= CheckPeriod)
            {
                var newCheckPeriod = TimeSpan.FromTicks(validityTime.Ticks / 4);
                if (newCheckPeriod < MinimumCheckPeriodForAutoAdjust)
                {
                    newCheckPeriod = MinimumCheckPeriodForAutoAdjust;
                }
                CheckPeriod = newCheckPeriod;
                return true;
            }
            return false;
        }

        public Task OnLoggingIn(LoggingInCtx authenticationCtx)
        {
            return Task.CompletedTask;
        }

        // Adjust the renewal period if a user's credential have a shorter expiration time than the current period.
        public Task OnLoggedIn(LoggedInCtx ctx)
        {
            foreach (var kvp in ctx.Session.AuthenticationExpirationDates)
            {
                if (AdjustCheckPeriod(kvp.Value, out var previousCheckPeriod))
                {
                    _logger.Log(LogLevel.Warn, "CredentialsRenewer.OnLoggedIn", "Credentials validity time is shorter than credentials renewal period ; overriding renewal period", new
                    {
                        PreviousRenewalPeriod = $"{previousCheckPeriod.TotalSeconds} seconds",
                        NewRenewalPeriod = $"{CheckPeriod.TotalSeconds} seconds",
                        AuthenticationProvider = kvp.Key,
                        User = ctx.Result
                    });
                }
            }

            return Task.CompletedTask;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _periodShortenedCts.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}

