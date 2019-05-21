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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Stormancer.Server.GameFinder
{
    class GameReadyCheck : IDisposable
    {
        public GameReadyCheck(int timeout, Action closeReadyCheck, Game game)
        {
            TimeoutTask = Task.Delay(timeout);
            _startDate = DateTime.UtcNow;
            _timeout = timeout;
            _closeReadyCheck = closeReadyCheck;
            PlayersReadyState = new Dictionary<string, Readiness>();
            foreach (var player in game.AllPlayers)
            {
                PlayersReadyState[player.UserId] = Readiness.Unknown;
            }
            Game = game;
        }

        private int _timeout;
        private DateTime _startDate;
        private Action _closeReadyCheck;

        public Game Game { get; set; }

        public Dictionary<string, Readiness> PlayersReadyState
        {
            get; private set;
        }

        public Task TimeoutTask { get; private set; }

        public bool IsSuccess { get; private set; }

        public bool RanToCompletion { get; private set; }

        private TaskCompletionSource<GameReadyCheckResult> _tcs = new TaskCompletionSource<GameReadyCheckResult>();
        public Task<GameReadyCheckResult> WhenCompleteAsync()
        {
            return _tcs.Task;
        }

        internal bool ContainsPlayer(string uId)
        {
            return PlayersReadyState.ContainsKey(uId);
        }

        internal void ResolvePlayer(string id, bool accepts)
        {
            if (!PlayersReadyState.ContainsKey(id))
            {
                return;
            }

            PlayersReadyState[id] = accepts ? Readiness.Ready : Readiness.NotReady;

            RaiseStateChanged();
            
            if (GlobalState != Readiness.Unknown)
            {
                ResolveReadyPhase();
            }
        }

        private void ResolveReadyPhase()
        {
            var globalState = GlobalState;
            if (globalState == Readiness.Unknown)
            {
                return;
            }
            else if (globalState == Readiness.Ready)
            {
                _tcs.TrySetResult(new GameReadyCheckResult(true, Enumerable.Empty<Group>(), Game.AllGroups));
            }
            else
            {
                var unReadyGroupList = new List<Group>();
                var readyGroupList = new List<Group>();
                foreach(var group in Game.AllGroups)
                {
                    if(group.Players.Any(p=>PlayersReadyState[p.UserId] == Readiness.NotReady))
                    {
                        unReadyGroupList.Add(group);
                    }
                    else
                    {
                        readyGroupList.Add(group);
                    }
                }
                _tcs.TrySetResult(new GameReadyCheckResult(false, unReadyGroupList, readyGroupList));
            }
        }

        public Readiness GlobalState
        {
            get
            {
                var checkState = Readiness.Unknown;
                if (PlayersReadyState.Values.Any(r => r == Readiness.NotReady))
                {
                    checkState = Readiness.NotReady;
                }
                if (PlayersReadyState.Values.All(r => r == Readiness.Ready))
                {
                    checkState = Readiness.Ready;
                }
                return checkState;
            }
        }
        private void RaiseStateChanged()
        {
            if (Game != null)
            {
                var state = new ReadyVerificationRequest()
                {
                    gameId = Game.Id,
                    members = PlayersReadyState,
                    timeout = _timeout - (int)(DateTime.UtcNow - _startDate).TotalMilliseconds
                };

                var e = StateChanged;
                if (e != null)
                {
                    e(state);
                }
            }
        }

        public void Dispose()
        {
            _closeReadyCheck();
        }

        internal Action<ReadyVerificationRequest> StateChanged { get; set; }

        internal void Cancel(string id)
        {
            ResolvePlayer(id, false);
        }
    }

    class GameReadyCheckResult
    {
        public GameReadyCheckResult(bool success, IEnumerable<Group> timeoutedGroups, IEnumerable<Group> readyGroups)
        {
            Success = success;
            UnreadyGroups = timeoutedGroups;
            ReadyGroups = readyGroups;
        }

        public bool Success { get; private set; }

        public IEnumerable<Group> UnreadyGroups { get; private set; }

        public IEnumerable<Group> ReadyGroups { get; private set; }
    }
}
