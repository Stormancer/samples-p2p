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

using Stormancer.Server.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Plugins.Configuration
{
    public interface IConfiguration
    {
        dynamic Settings { get; }

        event EventHandler<dynamic> SettingsChanged;
    }
    internal class DefaultConfiguration : IConfiguration, IDisposable
    {
        private event EventHandler<dynamic> SettingsChangedImpl;

        protected readonly IEnvironment _env;
        private bool _subscribed = false;
        public DefaultConfiguration(IEnvironment environment)
        {
            _env = environment;
            Settings = environment.Configuration;
        }



        public dynamic Settings
        {
            get;
            protected set;
        }

        private void RaiseSettingsChanged(object sender, dynamic args)
        {
            Settings = _env.Configuration;
            SettingsChangedImpl?.Invoke(this, Settings);
        }

        public event EventHandler<dynamic> SettingsChanged
        {
            add
            {
                if (!_subscribed)
                {
                    _subscribed = true;
                    _env.ConfigurationChanged += RaiseSettingsChanged;
                }
                SettingsChangedImpl += value;
            }
            remove
            {
                SettingsChangedImpl -= value;
            }
        }

        public virtual void Dispose()
        {
            if (_subscribed)
            {
                _subscribed = false;
                _env.ConfigurationChanged -= RaiseSettingsChanged;
            }
        }
    }

}


