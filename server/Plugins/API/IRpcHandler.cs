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
using Stormancer;
using Stormancer.Core;
using Stormancer.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Server.Plugins.API
{
    public class ApiCallContext<T>
    {
        internal ApiCallContext(T ctx, MethodInfo method, string route)
        {
            Context = ctx;
            Method = method;
            Route = route;
            Builder = builder => { };

        }
        public T Context { get; }
        public MethodInfo Method { get; }

        public string Route { get; }
        public Action<IDependencyBuilder> Builder { get; }


    }
    public interface IApiHandler
    {
        Task OnConnected(IScenePeerClient client);
        Task OnDisconnected(IScenePeerClient client);

        Task RunRpc(ApiCallContext<RequestContext<IScenePeerClient>> ctx, Func<ApiCallContext<RequestContext<IScenePeerClient>>, Task> next);
        Task RunRpc(ApiCallContext<RequestContext<IScenePeer>> ctx, Func<ApiCallContext<RequestContext<IScenePeer>>, Task> next);
        Task RunFF(ApiCallContext<Packet<IScenePeer>> ctx, Func<ApiCallContext<Packet<IScenePeer>>, Task> next);
        Task RunFF(ApiCallContext<Packet<IScenePeerClient>> ctx, Func<ApiCallContext<Packet<IScenePeerClient>>, Task> next);
    }
}
