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
using System.Threading.Tasks;

namespace Stormancer
{
    class MemoryCache<T> : IDisposable where T : class
    {
        private class CacheEntry
        {
            public CacheEntry(Task<T> content, DateTime expiresOn, Action onInvalidated)
            {
                async Task<T> GetContent(Task<T> c, Action invalidate)
                {
                    try
                    {
                        var r = await c;
                        if (r == null)
                        {
                            invalidate();
                        }
                        else
                        {
                            ExpiresOn = expiresOn;
                        }
                        return r;
                    }
                    catch (Exception)
                    {
                        invalidate();
                        throw;
                    }
                }
                Content = GetContent(content, onInvalidated);
                CreatedOn = DateTime.UtcNow;
                ExpiresOn = null;
                OnInvalidated = onInvalidated;
            }
            public Task<T> Content { get; }
            public DateTime CreatedOn { get; }
            public DateTime? ExpiresOn { get; private set; }
            public Action OnInvalidated { get; }
        }

        ConcurrentDictionary<string, CacheEntry> cache = new ConcurrentDictionary<string, CacheEntry>();
        private bool _running = true;

        public MemoryCache()
        {
            Task.Run(async () =>
            {

                while (_running)
                {
                    await Task.Delay(60000);
                    foreach (var entry in cache.Where(kvp => (kvp.Value.ExpiresOn ?? DateTime.MaxValue) < DateTime.UtcNow).ToArray())
                    {
                        entry.Value.OnInvalidated();
                    }
                }
            });
        }

        /// <summary>
        /// Get from cache or update the cache if not found or stale. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="cache"></param>
        /// <param name="addFunction"></param>
        /// <param name="invalidationDelay"></param>
        /// <returns></returns>
        public async Task<T> Get(string id, Func<string, Task<T>> addFunction, TimeSpan invalidationDelay)
        {

            var entry = cache.GetOrAdd(id, i => new CacheEntry(addFunction(i), DateTime.UtcNow + invalidationDelay, () => cache.TryRemove(id, out _)));
            return await entry.Content;
        }

        public void Remove(string id)
        {
            cache.TryRemove(id, out _);
        }

        public void Dispose()
        {
            _running = false;
        }
    }
}
