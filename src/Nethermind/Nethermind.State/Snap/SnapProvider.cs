using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Db;
using Nethermind.Logging;
using Nethermind.Serialization.Rlp;
using Nethermind.State.Proofs;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;

namespace Nethermind.State.Snap
{
    public class SnapProvider : ISnapProvider
    {
        private readonly ITrieStore _store;
        private readonly IDb _flatDb;
        private readonly IDbProvider _dbProvider;
        private readonly ILogManager _logManager;
        private readonly ILogger _logger;

        public ProgressTracker ProgressTracker { get; set; }

        public SnapProvider(ITrieStore store, ILogManager logManager)
        {
            _store = store;
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
            _logger = logManager.GetClassLogger();
            ProgressTracker = new(_logManager);
        }

        public SnapProvider(IDbProvider dbProvider, ILogManager logManager)
        {
            _dbProvider = dbProvider ?? throw new ArgumentNullException(nameof(dbProvider));

            _store = new TrieStore(
                _dbProvider.StateDb,
                No.Pruning,
                Persist.EveryBlock,
                logManager);

            _flatDb = _dbProvider.FlatDb;

            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
            _logger = logManager.GetClassLogger();
            ProgressTracker = new(_logManager);
        }


        public bool AddAccountRange(long blockNumber, Keccak expectedRootHash, Keccak startingHash, PathWithAccount[] accounts, byte[][] proofs = null)
        {
            StateTree tree = new(_store, _logManager);
            (bool moreChildrenToRight, IList<PathWithAccount> accountsWithStorage, IList<Keccak> codeHashes) =
                SnapProviderHelper.AddAccountRange(tree, blockNumber, expectedRootHash, startingHash, accounts, proofs);

            bool success = expectedRootHash == tree.RootHash;

            if (success)
            {
                foreach (var item in accountsWithStorage)
                {
                    ProgressTracker.EnqueueAccountStorage(item);
                }

                ProgressTracker.EnqueueCodeHashes(codeHashes);

                ProgressTracker.NextAccountPath = accounts[accounts.Length - 1].AddressHash;
                ProgressTracker.MoreAccountsToRight = moreChildrenToRight;
                
                foreach (PathWithAccount? pathWithAccount in accounts)
                {
                    Rlp rlp = pathWithAccount.Account is null ? null : pathWithAccount.Account.IsTotallyEmpty ? Rlp.Encode(Account.TotallyEmpty) : Rlp.Encode(pathWithAccount.Account);

                    _flatDb.Set(pathWithAccount.AddressHash, rlp.Bytes);
                    if (pathWithAccount.AddressHash.Bytes[30] == 0 && pathWithAccount.AddressHash.Bytes[31] == 0) _logger.Info($"FLATDB added node {pathWithAccount.AddressHash}");
                }
            }
            else
            {
                _logger.Warn($"SNAP - AddAccountRange failed, {blockNumber}:{expectedRootHash}, startingHash:{startingHash}");
            }

            return success;
        }

        public bool AddStorageRange(long blockNumber, PathWithAccount pathWithAccount, Keccak expectedRootHash, Keccak startingHash, PathWithStorageSlot[] slots, byte[][] proofs = null)
        {
            // TODO: use expectedRootHash (StorageRootHash from Account), it can change when PIVOT changes

            StorageTree tree = new(_store, _logManager);
            (Keccak? calculatedRootHash, bool moreChildrenToRight) =  SnapProviderHelper.AddStorageRange(tree, blockNumber, startingHash, slots, proofs:proofs);

            bool success = calculatedRootHash != Keccak.EmptyTreeHash;

            if (success)
            {
                if(moreChildrenToRight)
                {
                    StorageRange range = new()
                    {
                        Accounts = new[] { pathWithAccount },
                        StartingHash = slots.Last().Path
                    };

                    ProgressTracker.EnqueueStorageRange(range);
                }
            }
            else
            {
                _logger.Warn($"SNAP - AddStorageRange failed, {blockNumber}:{expectedRootHash}, startingHash:{startingHash}");

                if (startingHash > Keccak.Zero)
                {
                    StorageRange range = new()
                    {
                        Accounts = new[] { pathWithAccount },
                        StartingHash = startingHash
                    };

                    ProgressTracker.EnqueueStorageRange(range);
                }
                else
                {
                    ProgressTracker.EnqueueAccountStorage(pathWithAccount);
                }
            }

            return success;
        }

        public ICollection<Keccak> AddCodes(Keccak[] requestedHashes, byte[][] codes)
        {
            HashSet<Keccak> set = requestedHashes.ToHashSet();

            for (int i = 0; i < codes.Length; i++)
            {
                byte[] code = codes[i];
                Keccak codeHash = Keccak.Compute(code);

                if (set.Remove(codeHash))
                {
                    _dbProvider.CodeDb.Set(codeHash, code);
                }
            }

            return set;
        }
    }
}
