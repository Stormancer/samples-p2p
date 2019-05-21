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
using Newtonsoft.Json.Linq;
using Stormancer;
using Stormancer.Server.Components;
using Stormancer.Diagnostics;
using Nest;
using Server.Plugins.Users;
using Stormancer.Core.Helpers;
using Stormancer.Server.Database;

namespace Stormancer.Server.Users
{
    class ConflictException : Exception
    { }
    class UserService : IUserService
    {
        private readonly IESClientFactory _clientFactory;
        private readonly string _indexName;
        private readonly ILogger _logger;
        private readonly Lazy<IEnumerable<IUserEventHandler>> _eventHandlers;
        private readonly Random _random = new Random();

        private static bool _mappingChecked = false;
        private static AsyncLock _mappingCheckedLock = new AsyncLock();
        private async Task CreateUserMapping()
        {

            await (await Client<User>()).MapAsync<User>(m => m
                .DynamicTemplates(templates => templates
                    .DynamicTemplate("auth", t => t
                         .PathMatch("auth.*")
                         .MatchMappingType("string")
                         .Mapping(ma => ma.Keyword(s => s.Index()))
                        )
                    .DynamicTemplate("data", t => t
                         .PathMatch("userData.*")
                         .MatchMappingType("string")
                         .Mapping(ma => ma.Keyword(s => s.Index()))
                        )
                     )
                 );

            await (await Client<PseudoUserRelation>()).MapAsync<PseudoUserRelation>(m => m
                .Properties(pd => pd
                    .Keyword(kpd => kpd.Name(record => record.Id).Index())
                    .Keyword(kpd => kpd.Name(record => record.UserId).Index(false))
                    )
                );
        }


        public UserService(
          
            Database.IESClientFactory clientFactory,
            IEnvironment environment,
            ILogger logger,
            Lazy<IEnumerable<IUserEventHandler>> eventHandlers
            )
        {
            _indexName = (string)(environment.Configuration.users?.index) ?? "gameData";
            _eventHandlers = eventHandlers;
            _logger = logger;
            //_logger.Log(LogLevel.Trace, "users", $"Using index {_indexName}", new { index = _indexName });

            _clientFactory = clientFactory;

        }

        private async Task<Nest.IElasticClient> Client<T>()
        {
            var client = await _clientFactory.CreateClient<T>(_indexName);
            if (!_mappingChecked)
            {
                using (await _mappingCheckedLock.LockAsync())
                {
                    if (!_mappingChecked)
                    {
                        _mappingChecked = true;
                        await CreateUserMapping();
                    }
                }
            }
            return client;
        }

        private string GetIndex<T>()
        {
            return _clientFactory.GetIndex<T>(_indexName);
        }
        public async Task<User> AddAuthentication(User user, string provider, JObject authData, string cacheId)
        {
            var c = await Client<User>();

            user.Auth[provider] = authData;
            var result = await c.IndexAsync(new AuthenticationClaim { Id = provider + "_" + cacheId, UserId = user.Id }, s => s.Index(GetIndex<AuthenticationClaim>()).OpType(Elasticsearch.Net.OpType.Create));
            if(!result.IsValid) 
            {
                if (result.ServerError.Error.Type == "document_already_exists_exception")
                {
                    throw new ConflictException();
                }
                else
                {
                    throw new InvalidOperationException(result.ServerError.Error.Type);
                }
            }
            await c.IndexAsync(user, s => s.Index(GetIndex<User>()));
            
            return user;
        }

        public async Task<User> CreateUser(string id, JObject userData)
        {

            var user = new User() { Id = id, UserData = userData, };
            var esClient = await Client<User>();
            await esClient.IndexAsync(user, s => s);

            return user;
        }


        public async Task<User> GetUserByClaim(string provider, string claimPath, string login)
        {
            var c = await Client<User>();
            var cacheId = provider + "_" + login;
            var claim = await c.GetAsync<AuthenticationClaim>(cacheId, s => s.Index(GetIndex<AuthenticationClaim>()));
            if (claim.Found)
            {
                var r = await c.GetAsync<User>(claim.Source.UserId);
                if (r.Found)
                {
                    return r.Source;
                }
                else
                {
                    return null;
                }
            }
            else
            {
               
                var r = await c.SearchAsync<User>(sd => sd.Query(qd => qd.Term("auth." + provider + "." + claimPath, login)));



                User user;
                if (r.Hits.Count() > 1)
                {
                    user = await MergeUsers(r.Hits.Select(h => h.Source));
                }
                else
                {
                    var h = r.Hits.FirstOrDefault();


                    if (h != null)
                    {

                        user = h.Source;
                    }
                    else
                    {
                        return null;
                    }
                }
                await c.IndexAsync<AuthenticationClaim>(new AuthenticationClaim { Id = cacheId, UserId = user.Id }, s => s.Index(GetIndex<AuthenticationClaim>()));

                return user;
            }

        }

        private async Task<User> MergeUsers(IEnumerable<User> users)
        {
            var handlers = _eventHandlers.Value;
            foreach (var handler in handlers)
            {
                await handler.OnMergingUsers(users);
            }

            var sortedUsers = users.OrderBy(u => u.CreatedOn).ToList();
            var mainUser = sortedUsers.First();

            var data = new Dictionary<IUserEventHandler, object>();
            foreach (var handler in handlers)
            {
                data[handler] = await handler.OnMergedUsers(sortedUsers.Skip(1), mainUser);
            }

            var c = await Client<User>();
            _logger.Log(Stormancer.Diagnostics.LogLevel.Info, "users", "Merging users.", new { deleting = sortedUsers.Skip(1), into = mainUser });
            await c.BulkAsync(desc =>
            {
                desc = desc.DeleteMany<User>(sortedUsers.Skip(1).Select(u => u.Id))
                           .Index<User>(i => i.Document(mainUser));
                foreach (var handler in handlers)
                {
                    desc = handler.OnBuildMergeQuery(sortedUsers.Skip(1), mainUser, data[handler], desc);
                }
                return desc;
            });
            return mainUser;
        }

        public async Task<User> GetUser(string uid)
        {
            var c = await Client<User>();
            var r = await c.GetAsync<User>(uid);
            if (r.Source != null)
            {
                return r.Source;
            }
            else
            {
                return null;
            }
        }

        public async Task UpdateUserData<T>(string uid, T data)
        {
            var user = await GetUser(uid);
            if (user == null)
            {
                throw new InvalidOperationException($"Update failed: User {uid} not found.");
            }
            else
            {
                user.UserData = JObject.FromObject(data);
                await (await Client<User>()).IndexAsync(user,s=>s);
            }
        }

        public async Task<IEnumerable<User>> Query(string query, int take, int skip)
        {
            var c = await Client<User>();

            var result = await c.SearchAsync<User>(s => s.Query(q => q.QueryString(qs => qs.Query(query).DefaultField("userData.handle") )).Size(take).Skip(skip));

            return result.Documents;
        }

        public async Task UpdateCommunicationChannel(string userId, string channel, JObject data)
        {
            var user = await GetUser(userId);

            if (user == null)
            {
                throw new InvalidOperationException("User not found.");
            }
            else
            {
                user.Channels[channel] = JObject.FromObject(data);
                await (await Client<User>()).IndexAsync(user,s=>s);
            }

        }

        public async Task Delete(string id)
        {
            var user = await GetUser(id);
            if (user == null)
            {
                throw new InvalidOperationException("User not found");
            }

            var c = await Client<User>();

            var response = await c.DeleteAsync<User>(user.Id);

            if (!response.IsValid)
            {
                throw new InvalidOperationException("DB error : " + response.ServerError.ToString());
            }
        }


    }
}
