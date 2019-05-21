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
using SmartFormat.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Server.Database
{
    internal class TimeIntervalFormatter : IFormatter
    {
       
        private string[] _names = new string[] { "interval" };
        public string[] Names
        {
            get
            {
                return _names;
            }

            set
            {
                _names = value;
            }
        }

        public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
        {
            if (!(formattingInfo.CurrentValue is DateTime))
            {
                return false;
            }
            DateTime date = (DateTime)formattingInfo.CurrentValue;

            int interval;
            if (!int.TryParse(formattingInfo.FormatterOptions, out interval))
            {
                return false;
            }

            var ts = date.Ticks / (TimeSpan.TicksPerHour * interval);
            formattingInfo.Write(ts.ToString());
            return true;

        }
    }
}
