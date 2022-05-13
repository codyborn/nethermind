using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethermind.Core.Crypto;
using Nethermind.Trie;

namespace Nethermind.State.Snap.Storage
{
    public class SnapLayer : SortedDictionary<Keccak, TrieNode>
    {

    }
}
