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
using Stormancer.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    /// <summary>
    /// Provides a way to store actions that can be called once accross nodes.
    /// </summary>
    interface IActionStore
    {
        /// <summary>
        /// Runs the action with the id provided as parameter.
        /// </summary>
        /// <param name="id">The id of the action</param>
        /// <returns>true if an action was executed, false otherwise</returns>
        bool TryRun(string id);

        IDisposable RegisterAction(string id, Action action);
    }

    internal class SingleNodeActionStore : IActionStore
    {
        private readonly ConcurrentDictionary<string, Action> _actions = new ConcurrentDictionary<string, Action>();
        private readonly Func<ILogger> _logger;

        private class Disposable : IDisposable
        {
            private string id;
            private ConcurrentDictionary<string, Action> actions;

            public Disposable(ConcurrentDictionary<string, Action> actions, string id)
            {
                this.id = id;
                this.actions = actions;
            }

            public void Dispose()
            {
                Action action;
                actions.TryRemove(id, out action);
            }
        }
        public SingleNodeActionStore(Func<ILogger> logger)
        {
            _logger = logger;
        }
        public IDisposable RegisterAction(string id, Action action)
        {
            if (!_actions.TryAdd(id, action))
            {
                throw new ArgumentException($"An action is already registered with id '{id}'");
            }
            return new Disposable(_actions, id);
        }

        public bool TryRun(string id)
        {
            Action action;
            var success = _actions.TryRemove(id, out action);
            if (success)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _logger().Log(LogLevel.Error, "actionStore", $"An error occured while running the action '{id}'", ex);
                }
            }
            return success;
        }
    }

}
