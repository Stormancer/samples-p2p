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
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace Stormancer.Server.Users
{
    public class UsersAdminController : ApiController
    {
        private readonly IUserService _users;
        private readonly IAuthenticationService auth;

        public UsersAdminController(IUserService users, IAuthenticationService auth)
        {
            _users = users;
            this.auth = auth;
        }

        [HttpGet]
        [ActionName("getByClaim")]
        public async Task<User> GetByClaim(string provider, string claimPath, string claimValue)
        {
            return await _users.GetUserByClaim(provider, claimPath, claimValue);
        }

        [HttpGet]
        [ActionName("search")]
        public async Task<IEnumerable<UserViewModel>> Search(HttpRequestMessage request , int take = 20, int skip = 0)
        {
            var query = request.GetQueryNameValuePairs();
            var users = await _users.Query(query, take, skip);

            return users.Select(user => new UserViewModel { id = user.Id });
        }
        [HttpGet]
        public Task<User> Get(string id)
        {
            return _users.GetUser(id);
        }
        [HttpDelete]
        public Task Delete(string id)
        {
            return _users.Delete(id);
        }

        [HttpDelete]
        public async Task Unlink(string userId, string provider)
        {
            var user = await _users.GetUser(userId);
            await auth.Unlink(user,provider);
        }

   
    }


    public class UserViewModel
    {
        public string id { get; set; }

    }
}
