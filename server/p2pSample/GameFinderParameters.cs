using MsgPack.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2p
{
    public class GameFinderParameters
    {
        [MessagePackMember(0)]
        public string GameId { get; set; }
    }
}
