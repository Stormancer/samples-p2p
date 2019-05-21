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
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Stormancer.Plugins.ServiceLocator
{
    public class ServiceLocationCtx
    {
        /// <summary>
        /// Type of the service
        /// </summary>
        public string ServiceType { get; set; }

        /// <summary>
        /// Name of the specific instance of the service we are trying to locate
        /// </summary>
        public string ServiceName { get; set; }

        public string SceneId { get; set; }

        public Dictionary<string, string> Context { get; } = new Dictionary<string, string>();

    }

    /// <summary>
    /// Provides extensibility points for the service locator
    /// </summary>
    public interface IServiceLocatorProvider
    {
        /// <summary>
        /// Called before the default service location logic is called.
        /// </summary>
        /// <param name="ctx">A context object that provides details about the current service resolution.</param>
        /// <returns></returns>
        Task OnLocatingService(ServiceLocationCtx ctx);
    }
}
