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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer
{
    /// <summary>
    /// Retry policies to use with Retries.Retry
    /// </summary>
    public static class RetryTimings
    {
        /// <summary>
        /// A retry policy that waits the same time between the tries
        /// </summary>
        /// <param name="tries">The number of times the opertaion will be tried</param>
        /// <param name="delay">The delay between the operations</param>
        /// <returns>A retry policy to use with Retries.Retry</returns>
        public static IEnumerable<TimeSpan> ConstantDelay(int tries, TimeSpan delay) => new ConstantDelayRetry(tries, delay);

        /// <summary>
        /// A retry policy that waits longer and longer between the tries
        /// </summary>
        /// <param name="tries">The number of times the opertaion will be tried</param>
        /// <param name="delay">The delay increment between the first two tries. The delay will be incremented by the same value between each try.</param>
        /// <returns>A retry policy to use with Retries.Retry</returns>
        public static IEnumerable<TimeSpan> IncrementalDelay(int tries, TimeSpan delay) => new IncrementalDelayRetry(tries, delay);

        private class ConstantDelayRetry : IEnumerable<TimeSpan>
        {
            private readonly int _tryCount;
            private readonly TimeSpan _delay;

            public ConstantDelayRetry(int tryCount, TimeSpan delay)
            {
                _tryCount = tryCount;
                _delay = delay;
            }

            public IEnumerator<TimeSpan> GetEnumerator()
            {
                for (var i = 1; i < _tryCount; i++)
                {
                    yield return _delay;
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private class IncrementalDelayRetry : IEnumerable<TimeSpan>
        {
            private readonly int _tryCount;
            private readonly TimeSpan _delay;

            public IncrementalDelayRetry(int tryCount, TimeSpan delay)
            {
                _tryCount = tryCount;
                _delay = delay;
            }

            public IEnumerator<TimeSpan> GetEnumerator()
            {
                for (var i = 1; i < _tryCount; i++)
                {
                    yield return TimeSpan.FromTicks(_delay.Ticks * i);
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
