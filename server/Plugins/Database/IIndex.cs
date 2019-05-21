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

namespace Stormancer.Server.Database
{

    public interface IIndex<TValue>
    {
        Task<bool> TryAdd(string key, TValue value);

        Task<Result<TValue>> GetOrAdd(string key, TValue value);

        Task<Result<TValue>> TryGet(string key);

        Task<Result<TValue>> TryRemove(string key);

        /// <summary>
        /// Tries to update the value associated with the specific keys, using optimistic concurrency.
        /// </summary>
        /// <param name="key">The key to update</param>
        /// <param name="value">The new value</param>
        /// <param name="version">The version of the value that must be updated</param>
        /// <returns>The result indicates successful if the record was updated, but always returns the latest known value with its version.</returns>
        Task<Result<TValue>> TryUpdate(string key, TValue value, int version);

        int Count
        {
            get;
        }


    }
    //TODO: Upgrade to distributed algorithm
    public class InMemoryIndex<TValue> : IIndex<TValue>
    {
        private struct Container<T>
        {
            public Container(T value, int version)
            {
                Value = value;
                Version = version;
            }
            public T Value { get; private set; }
            public int Version { get; private set; }

            public override bool Equals(object obj)
            {
                if (obj is Container<T>)
                {
                    return ((Container<T>)obj).Version == Version;
                }
                else
                {
                    return false;
                }
            }

            public override int GetHashCode()
            {
                return Value.GetHashCode();
            }
        }
        private readonly ConcurrentDictionary<string, Container<TValue>> _dictionary = new ConcurrentDictionary<string, Container<TValue>>();
        public Task<bool> TryAdd(string key, TValue value)
        {
            return Task.FromResult(_dictionary.TryAdd(key, new Container<TValue>(value, 0)));
        }

        public Task<Result<TValue>> GetOrAdd(string key, TValue value)
        {
            var container = _dictionary.GetOrAdd(key, new Container<TValue>(value, 0));

            return Task.FromResult(new Result<TValue>(container.Value, true, container.Version));
        }
        public Task<Result<TValue>> TryGet(string key)
        {
            Container<TValue> value;
            var found = _dictionary.TryGetValue(key, out value);
            return Task.FromResult(new Result<TValue>(value.Value, found, value.Version));
        }


        public Task<Result<TValue>> TryRemove(string key)
        {
            Container<TValue> value;
            var success = _dictionary.TryRemove(key, out value);
            return Task.FromResult(new Result<TValue>(value.Value, success, value.Version));
        }

        public Task<Result<TValue>> TryUpdate(string key, TValue value, int version)
        {
            if (_dictionary.TryUpdate(key, new Container<TValue>(value, version + 1), new Container<TValue>(default(TValue), version)))
            {
                return Task.FromResult(new Result<TValue>(value, true, version + 1));
            }
            else
            {
                Container<TValue> c;
                var found = _dictionary.TryGetValue(key, out c);

                return Task.FromResult(new Result<TValue>(found ? c.Value : default(TValue), false, found ? c.Version : -1));
            }
        }

        public int Count
        {
            get
            {
                return _dictionary.Count;
            }
        }
    }

    public struct Result<T>
    {
        internal Result(T value, bool found, int version)
        {
            Value = value;
            Success = found;
            Version = version;
        }
        public T Value { get; private set; }

        public bool Success { get; private set; }

        public int Version { get; private set; }
    }
}
