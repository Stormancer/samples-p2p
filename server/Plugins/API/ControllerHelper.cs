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
using Stormancer.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Plugins
{
    public static class ControllerHelper
    {
        public static Func<RequestContext<IScenePeerClient>, Task> ToAction<TData, TResult>(Func<TData, Task<TResult>> typedAction)
        {
            return async (RequestContext<IScenePeerClient> request) =>
            {
                await request.SendValue(await typedAction(request.ReadObject<TData>()));
            };
        }

        public static Func<RequestContext<IScenePeerClient>, Task> ToAction<TData>(Func<TData, Task> typedAction)
        {
            return (RequestContext<IScenePeerClient> request) =>
            {
                return typedAction(request.ReadObject<TData>());
            };
        }

        public static Func<RequestContext<IScenePeerClient>, Task> ToActionWithUserData<TUserData, TData, TResult>(Func<TUserData, TData, Task<TResult>> typedAction)
        {
            return async (RequestContext<IScenePeerClient> request) =>
            {
                var userData = request.RemotePeer.GetUserData<TUserData>();

                await request.SendValue(await typedAction(userData, request.ReadObject<TData>()));
            };
        }

        public static Func<RequestContext<IScenePeerClient>, Task> ToActionWithUserData<TUserData, TData>(Func<TUserData, TData, Task> typedAction)
        {
            return (RequestContext<IScenePeerClient> request) =>
            {
                var userData = request.RemotePeer.GetUserData<TUserData>();

                return typedAction(userData, request.ReadObject<TData>());
            };
        }

        public static Func<RequestContext<IScenePeerClient>, Task> ToActionWithUserData<TUserData, TResult>(Func<TUserData, Task<TResult>> typedAction)
        {
            return async (RequestContext<IScenePeerClient> request) =>
            {
                var logger = request.RemotePeer.Host.DependencyResolver.Resolve<Diagnostics.ILogger>();

                var userData = request.RemotePeer.GetUserData<TUserData>();

                var result = await typedAction(userData);

                await request.SendValue(result);

            };
        }

        public static Func<RequestContext<IScenePeerClient>, Task> ToActionWithUserData<TUserData>(Func<TUserData, Task> typedAction)
        {
            return (RequestContext<IScenePeerClient> request) =>
            {
                var userData = request.RemotePeer.GetUserData<TUserData>();

                return typedAction(userData);
            };
        }
    }
}
