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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Plugins.API
{
    public abstract class ControllerBase
    {
        public RequestContext<IScenePeerClient> Request { get; internal set; }
        public CancellationToken CancellationToken { get; internal set; }
        public IScenePeer Peer { get; internal set; }
        public bool IsS2S
        {
            get
            {
                return !(Peer is IScenePeerClient);
            }
        }
        /// <summary>
        /// Handles exceptions uncatched by the route itself.
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns>A task that returns true if the exception was handled, false otherwise.</returns>
        protected internal virtual Task<bool> HandleException(ApiExceptionContext ctx)
        {
            return Task.FromResult(false);
        }

        protected internal virtual Task OnConnecting(IScenePeerClient client)
        {
            return Task.CompletedTask;
        }

        protected internal virtual Task OnConnectionRejected(IScenePeerClient client)
        {
            return Task.CompletedTask;
        }

        protected internal virtual Task OnConnected(IScenePeerClient peer)
        {
            return Task.FromResult(true);
        }

        protected internal virtual Task OnDisconnected(DisconnectedArgs args)
        {
            return Task.FromResult(true);
        }
    }
}
