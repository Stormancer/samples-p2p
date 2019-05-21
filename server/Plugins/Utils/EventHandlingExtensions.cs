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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer
{
    public static class EventHandlingExtensions
    {
        //public static async Task RunEventHandler<T>(this IEnumerable<T> eh, Func<T, Task> action, Action<Exception> errorHandler)
        //{
        //    if (eh == null)
        //    {
        //        throw new ArgumentNullException("eh");
        //    }
        //    if (action == null)
        //    {
        //        throw new ArgumentNullException("action");

        //    }
        //    if (errorHandler == null)
        //    {
        //        throw new ArgumentNullException("errorHandler");

        //    }
        //    foreach (var h in eh)
        //    {
        //        try
        //        {
        //            await action(h);
        //        }
        //        catch (Exception ex)
        //        {
        //            errorHandler(ex);
        //            throw;
        //        }
        //    }
        //}

        public static void RunEventHandler<T>(this IEnumerable<T> eh, Action<T> action, Action<Exception> errorHandler)
        {
            if (eh == null)
            {
                throw new ArgumentNullException("eh");
            }
            if (action == null)
            {
                throw new ArgumentNullException("action");

            }
            if (errorHandler == null)
            {
                throw new ArgumentNullException("errorHandler");

            }
            foreach (var h in eh)
            {
                try
                {
                    action(h);
                }
                catch (Exception ex)
                {
                    errorHandler(ex);
                    throw;
                }
            }
        }
    }
}
