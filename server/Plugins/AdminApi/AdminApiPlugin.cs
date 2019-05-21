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
using Stormancer.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Owin;
using System.Web.Http.Dependencies;

namespace Server.Plugins.AdminApi
{
    class AdminApiPlugin : IHostPlugin
    {
        public void Build(HostPluginBuildContext ctx)
        {
            ctx.HostStarting += (Stormancer.Server.IHost host) =>
            {

                host.RegisterAdminApiFactory((builder, scene) =>
                {

                    var type = typeof(Microsoft.Owin.Builder.AppBuilder);
                    var config = new HttpConfiguration();
                    config.DependencyResolver = new DependencyResolver(scene.DependencyResolver);
                    var configurators = host.DependencyResolver.ResolveAll<IAdminWebApiConfig>();
                    foreach(var c in configurators)
                    {
                        c.Configure(config);
                    }
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                   
                    builder.UseWebApi(config);
                });

            };
        }
    }

    public class DependencyResolver : System.Web.Http.Dependencies.IDependencyResolver
    {
        private readonly Stormancer.IDependencyResolver _resolver;
        private readonly DependencyScope _mainScope;
        public DependencyResolver(Stormancer.IDependencyResolver resolver)
        {
            _resolver = resolver;
            _mainScope = new DependencyScope(resolver);
        }

        public IDependencyScope BeginScope()
        {
            return new DependencyScope(_resolver.CreateChild());

        }

        public void Dispose()
        {

        }

        public object GetService(Type serviceType)
        {
            return _mainScope.GetService(serviceType);
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            return _mainScope.GetServices(serviceType);
        }

        private class DependencyScope : IDependencyScope
        {
            private readonly Stormancer.IDependencyResolver _resolver;
            public DependencyScope(Stormancer.IDependencyResolver resolver)
            {
                _resolver = resolver;
            }
            public void Dispose()
            {
                _resolver.Dispose();
            }

            public object GetService(Type serviceType)
            {
                return _resolver.GetType().GetMethod("Resolve").MakeGenericMethod(serviceType).Invoke(_resolver, new object[] { });
            }

            public IEnumerable<object> GetServices(Type serviceType)
            {

                return (IEnumerable<object>)_resolver.GetType().GetMethod("ResolveAll").MakeGenericMethod(serviceType).Invoke(_resolver, new object[] { });


            }
        }
    }
}
