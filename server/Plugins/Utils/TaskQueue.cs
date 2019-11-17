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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Plugins.Utils
{
    /// <summary>
    /// A queue-like structure that executes asynchronous work sequentially.
    /// </summary>
    public class TaskQueue
    {
        /// <summary>
        /// Push work onto the task queue.
        /// </summary>
        /// <remarks>
        /// The work may be asynchronous.
        /// </remarks>
        /// <param name="work">Function that will be run by the task queue.</param>
        /// <returns>
        /// A Task that completes when <paramref name="work"/> has been run.
        /// If <paramref name="work"/> threw an exception, it will be contained in the Task.
        /// </returns>
        public Task PushWork(Func<Task> work)
        {
            lock (_currentWorkLock)
            {
                _currentWork = _currentWork.ContinueWith(_ => work()).Unwrap();

                return _currentWork;
            }
        }

        /// <summary>
        /// Push work that returns a result onto the task queue.
        /// </summary>
        /// <remarks>
        /// The work may be asynchronous.
        /// </remarks>
        /// <param name="work">Function that will be run by the task queue.</param>
        /// <returns>
        /// A Task that completes when <paramref name="work"/> has been run, and contains its result.
        /// If <paramref name="work"/> threw an exception, it will be contained in the Task.
        /// </returns>
        public Task<TResult> PushWork<TResult>(Func<Task<TResult>> work)
        {
            lock (_currentWorkLock)
            {
                var myWork = _currentWork.ContinueWith(_ => work()).Unwrap();
                _currentWork = myWork;

                return myWork;
            }
        }

        private object _currentWorkLock = new object();
        private Task _currentWork = Task.CompletedTask;
    }
}

