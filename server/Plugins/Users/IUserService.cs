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

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Stormancer.Server.Users
{
    public interface IUserService
    {
        Task<IEnumerable<User>> Query(IEnumerable<KeyValuePair<string,string>> query, int take, int skip);

        Task<User> GetUser(string uid);
        Task<User> AddAuthentication(User user, string provider, Action<dynamic> authDataModifier, Dictionary<string,string> cacheEntries);
        Task<User> RemoveAuthentication(User user, string provider);
        Task<User> GetUserByClaim(string provider, string claimPath, string login);
        Task<IEnumerable<User>> GetUsersByClaim(string provider, string claimPath, string[] logins);
        Task<User> CreateUser(string uid, JObject userData);


        Task UpdateUserData<T>(string uid, T data);

        Task UpdateCommunicationChannel(string userId, string channel, JObject data);
        Task Delete(string id);

        Task UpdateLastLoginDate(string userId);
        Task<Dictionary<string, User>> GetUsers(params string[] userIds);
    }
}
