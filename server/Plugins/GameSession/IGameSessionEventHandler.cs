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
using Stormancer.Core;
using Stormancer.Server.GameSession.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Stormancer.Server.GameSession
{
    public interface IGameSessionEventHandler
    {
        Task GameSessionStarting(GameSessionContext ctx);
        Task GameSessionStarted(GameSessionStartedCtx ctx);

        Task GameSessionCompleted(GameSessionCompleteCtx ctx);
    }

    public class GameSessionContext
    {
        public GameSessionContext(ISceneHost scene, GameSessionConfiguration config, IGameSessionService service)
        {
            Scene = scene;
            Config = config;
            Service = service;
        }

        public ISceneHost Scene { get; }
        public string Id { get => Scene.Id; }
        public string DedicatedServerPath { get; set; }
        public GameSessionConfiguration Config { get; }
        public IGameSessionService Service { get; }
    }

    public class GameSessionStartedCtx : GameSessionContext
    {
        public GameSessionStartedCtx(IGameSessionService service, ISceneHost scene, IEnumerable<Player> peers, GameSessionConfiguration config) : base(scene, config, service)
        {
            Peers = peers;
        }
        public IEnumerable<Player> Peers { get; }


    }


    public class Player
    {
        public Player(IScenePeerClient client, string uid)
        {
            UserId = uid;
            Peer = client;
        }
        public IScenePeerClient Peer { get; }

        public string UserId { get; }
    }

    public class GameSessionCompleteCtx : GameSessionContext
    {
        public GameSessionCompleteCtx(IGameSessionService service, ISceneHost scene, GameSessionConfiguration config, IEnumerable<GameSessionResult> results, IEnumerable<string> players) : base(scene, config, service)
        {
            Results = results;
            PlayerIds = players;
            ResultsWriter = (p, s) => { };
        }

        public IEnumerable<GameSessionResult> Results { get; }

        public IEnumerable<string> PlayerIds { get; }
        public Action<Stream, ISerializer> ResultsWriter { get; set; }
    }

    public class GameSessionResult
    {
        public GameSessionResult(string userId, IScenePeerClient client, Stream data)
        {
            Peer = client;
            Data = data;
            UserId = userId;
        }
        public IScenePeerClient Peer { get; }

        public string UserId { get; }

        public Stream Data { get; }


    }

}
