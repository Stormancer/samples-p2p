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
