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
using System.Net.Http;
using System.Web.Http;

namespace Server.Helpers
{
    public class TimestampHelper
    {
        static public DateTime UnixTimeStampSecondToDateTime(long unixTimeStamp)
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixTimeStamp).DateTime;
        }

        static public long DateTimeToUnixTimeStamp(DateTime date)
        {
            DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            TimeSpan t = date - UnixEpoch;
            return (long)t.TotalSeconds;
        }
    }

    public class HttpHelper
    {
        /// <summary>
        /// Custom http error emcapsulation.
        /// </summary>
        /// <param name="statusCode">Http status code</param>
        /// <param name="reasonPhrase">Reason phrase send to client</param>
        /// <returns></returns>
        static public HttpResponseException HttpError(System.Net.HttpStatusCode statusCode, string reasonPhrase)
        {
            return new HttpResponseException(new HttpResponseMessage(statusCode) { ReasonPhrase = reasonPhrase });
        }
    }
}
