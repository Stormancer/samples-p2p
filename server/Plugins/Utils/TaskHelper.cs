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
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer
{
    /// <summary>
    /// Helper methods for frequent operations on Tasks
    /// </summary>
    public class Retries
    {
        /// <summary>
        /// Retries the same operation using the provided delayPolicy
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="delayPolicy"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static Task Retry(Func<int,Task> operation, IEnumerable<TimeSpan> delayPolicy, CancellationToken cancellationToken)
        {
            return Retry(operation, delayPolicy, cancellationToken, ex => true);
        }
        /// <summary>
        /// Retries the same operation using the provided delayPolicy
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="delayPolicy"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="canRetry">Filter used to decide if we should retry or not after an error.</param>
        /// <returns></returns>
        public static Task Retry(Func<int, Task> operation, IEnumerable<TimeSpan> delayPolicy, CancellationToken cancellationToken, Func<Exception, bool> canRetry)
        {
            return Retry<bool>(async (retry) =>
            {
                await operation(retry);
                return true;
            }, delayPolicy, cancellationToken, canRetry);
        }

        /// <summary>
        /// Retries the same operation using the provided delayPolicy
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="delayPolicy"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="canRetry">Filter used to decide if we should retry or not after an error.</param>
        /// <returns></returns>
        public async static Task<T> Retry<T>(Func<int,Task<T>> operation, IEnumerable<TimeSpan> delayPolicy, CancellationToken cancellationToken, Func<Exception, bool> canRetry)
        {
            var enumerator = delayPolicy.GetEnumerator();
            var exceptions = new List<Exception>();
            int i = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await operation(i);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    if (!enumerator.MoveNext())
                    {
                        throw new AggregateException(exceptions);
                    }
                }
                i++;
                await Task.Delay(enumerator.Current);
            }
        }

        public static Task<T> Retry<T>(Func<int,T> operation, IEnumerable<TimeSpan> delayPolicy, CancellationToken cancellationToken, Func<Exception, bool> canRetry)
            => Retry((@try) => Task.FromResult(operation(@try)), delayPolicy, cancellationToken, canRetry);

        public static Task Retry(Action operation, IEnumerable<TimeSpan> delayPolicy, CancellationToken cancellationToken) =>
            Retry((@try) =>
            {
                operation();
                return Task.FromResult(true);
            }, delayPolicy, cancellationToken);

        public static async Task<T> Retry<T>(Func<Task<T>> operation, int maxRetries, TimeSpan maxTotalTime, CancellationToken cancellationToken, Func<Exception, Task<DateTimeOffset?>> shouldRetry)
        {
            Contract.Requires(maxRetries >= 0);
            var maxDate = DateTimeOffset.UtcNow + maxTotalTime;
            var exceptions = new List<Exception>();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await operation();
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                    var nextRetry = await shouldRetry(exception);
                    if (nextRetry.HasValue && nextRetry.Value <= maxDate && maxRetries > 0)
                    {
                        maxRetries--;
                        await Task.Delay(nextRetry.Value - DateTimeOffset.UtcNow);
                    }
                    else
                    {
                        throw new AggregateException(exceptions);
                    }
                }
            }
        }
    }
}
