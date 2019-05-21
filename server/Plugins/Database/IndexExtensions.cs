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
using Stormancer.Server.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server
{
    public static class IndexExtensions
    {

        public static async Task<Result<TValue>> AddOrUpdateWithRetries<TValue>(this IIndex<TValue> index, string key, TValue v, Func<TValue, TValue> mutator, int retries = 32)
        {
            var success = false;
            var result = await index.GetOrAdd(key, v);

            if(result.Version == 0)//Just added new value
            {
                return result;
            }
            var group = result.Value;
            var version = result.Version;
            var i = 0;
            while (!success && i < retries)
            {
                i++;
                var newValue = mutator(group);
                result = await index.TryUpdate(key, newValue, version);
                success = result.Success;
                group = result.Value;
                version = result.Version;
                if (version == -1)
                {
                    throw new InvalidOperationException($"'{key}' was removed from the index.");
                }
            }
            return result;

        }
        /// <summary>
        /// Updates a record using a mutator, with retry semantic in case of optimistic concurrency failure.
        /// </summary>
        /// <remarks>An exception thrown in the mutator will be thrown by the method. This way you can cancel an update in the mutator.</remarks>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="index"></param>
        /// <param name="key"></param>
        /// <param name="mutator"></param>
        /// <param name="retries"></param>
        /// <returns></returns>
        public static async Task<Result<TValue>> UpdateWithRetries<TValue>(this IIndex<TValue> index, string key, Func<TValue, TValue> mutator, int retries = 32)
        {
            var success = false;
            var result = await index.TryGet(key);

            if (!result.Success)
            {
                throw new ArgumentException($"'{key}' does not exist in the index.");
            }
            var group = result.Value;
            var version = result.Version;
            var i = 0;
            while (!success && i < retries)
            {
                i++;
                var newValue = mutator(group);
                result = await index.TryUpdate(key, newValue, version);
                success = result.Success;
                group = result.Value;
                version = result.Version;
                if (version == -1)
                {
                    throw new InvalidOperationException($"'{key}' was removed from the index.");
                }
            }
            return result;

        }
    }
}
